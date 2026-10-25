using Core.Rules;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Builds and refreshes the GameObject of every object type from code (no prefab assets): mesh, materials, collider
    /// and rigidbody. Default sizes are real-world: poker cards 63.5 × 88.9 mm, 16 mm dice, 25 mm tokens.
    /// </summary>
    public static class ObjectViewFactory
    {
        public const float CardThickness = 0.0004f;
        public const float CardMass = 0.0018f;
        public const float TokenDiameter = 0.025f;
        public const float TokenThickness = 0.003f;
        public const float TileSize = 0.05f;
        public const float TileThickness = 0.003f;
        public const float BagRadius = 0.04f;
        public const float BagHeight = 0.08f;
        public const float BoardThickness = 0.004f;
        public const float PdfThickness = 0.003f;
        public static readonly Vector2 BoardDefaultSize = new Vector2(0.5f, 0.5f);
        public const float FigureWidth = 0.03f;
        public const float FigureDefaultHeight = 0.05f;
        public const float FigureThickness = 0.002f;
        public const float ModelPlaceholderSize = 0.03f;

        private static readonly Color Ivory = new Color(0.95f, 0.93f, 0.87f);
        private static readonly Color CardEdge = new Color(0.92f, 0.91f, 0.88f);
        private static readonly Color BagCloth = new Color(0.45f, 0.3f, 0.18f);
        private static readonly Color InfiniteBagCloth = new Color(0.33f, 0.2f, 0.45f);
        private static readonly Color Placeholder = new Color(0.55f, 0.55f, 0.6f);
        private static PhysicsMaterial _diceMaterial;

        /// <summary>Hard plastic on felt: a little bounce, moderate friction, so dice tumble and then settle on a face.</summary>
        private static PhysicsMaterial DiceMaterial => _diceMaterial != null ? _diceMaterial : _diceMaterial = new PhysicsMaterial("Dice")
        {
            bounciness = 0.25f,
            dynamicFriction = 0.35f,
            staticFriction = 0.5f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
        };

        /// <summary>Overlapping bodies (a spawn, a teleport) separate at most this fast instead of exploding apart.</summary>
        public const float MaxDepenetrationSpeed = 0.25f;
        /// <summary>No object ever moves faster than this, whatever the solver does.</summary>
        public const float MaxLinearSpeed = 3f;
        /// <summary>Cards and decks: a tossed card slides a few centimetres and stops, as on cloth.</summary>
        public const float CardLinearDamping = 1.5f;
        public const float CardAngularDamping = 3f;

        private static PhysicsMaterial _cardMaterial;

        /// <summary>Card stock on felt: no bounce, enough friction that cards do not glide.</summary>
        private static PhysicsMaterial CardMaterial => _cardMaterial != null ? _cardMaterial : _cardMaterial = new PhysicsMaterial("Card")
        {
            bounciness = 0f,
            dynamicFriction = 0.55f,
            staticFriction = 0.7f,
            bounceCombine = PhysicsMaterialCombine.Minimum,
            frictionCombine = PhysicsMaterialCombine.Maximum,
        };

        /// <summary>Counter and clock blocks (G27), display on top.</summary>
        public static readonly Vector3 CounterSize = new Vector3(0.07f, 0.02f, 0.035f);
        public static readonly Vector3 ClockSize = new Vector3(0.09f, 0.022f, 0.04f);
        private static readonly Color DisplayCase = new Color(0.12f, 0.13f, 0.15f);

        public static ObjectView Create(TableObject model, AssetLibrary assets, Transform parent)
        {
            var root = new GameObject($"{model.Type} {model.Id}");
            root.transform.SetParent(parent, false);

            var visual = root.transform;
            if (model is Figure)
            {
                // A standee is a thin box standing up: its +Y (image) face turned towards −Z, image top towards +Y.
                visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                visual.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }

            var filter = visual.gameObject.AddComponent<MeshFilter>();
            var renderer = visual.gameObject.AddComponent<MeshRenderer>();
            var body = root.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = MaxDepenetrationSpeed;
            body.maxLinearVelocity = MaxLinearSpeed;

            var view = root.AddComponent<ObjectView>();
            view.Init(model, visual, filter, renderer, body);
            Refresh(view, assets);
            view.SnapToState();
            return view;
        }

        /// <summary>Re-reads the model: mesh (deck height), materials and cells, collider, mass, lock state.</summary>
        public static void Refresh(ObjectView view, AssetLibrary assets)
        {
            var model = view.Model;
            var tint = model.Tint.ToColor(Color.white);
            switch (model)
            {
                case Card card:
                    RefreshCard(view, assets, card);
                    break;
                case Deck deck:
                    RefreshDeck(view, assets, deck);
                    break;
                case Token token:
                {
                    // A counted stack (G14) is one body, as tall as its pieces.
                    var size = (token.Thickness ?? TokenThickness) * token.Count;
                    // A TTS token is shaped by its image: it takes the image's proportions.
                    var face2 = token.Shape == TokenShape.Image ? Proportioned(new Vector2(TokenDiameter, TokenDiameter), assets.Texture(token.Asset)) : new Vector2(TokenDiameter, TokenDiameter);
                    var mesh = token.Shape == TokenShape.Round
                        ? MeshFactory.ImageCylinder(TokenDiameter / 2f, size)
                        : MeshFactory.ImageBox(new Vector3(face2.x, size, face2.y));
                    var face = assets.ImageMaterial(token.Asset, model.Tint.ToColor(Ivory));
                    var edge = model.Tint.ToColor(Ivory) * 0.85f;
                    SetPrism(view, mesh, face, face, token.Count > 1 ? assets.Grooved(edge) : assets.Plain(edge), 0.002f * token.Count);
                    SetCell(view, assets, MeshFactory.SideSubmesh, new Vector4(1f, token.Count, 0f, 0f));
                    break;
                }
                case Tile tile:
                {
                    var thickness = (tile.Thickness ?? TileThickness) * tile.Count;
                    var area = tile.Size is { } tileSize ? new Vector2(tileSize.X, tileSize.Y) : new Vector2(TileSize, TileSize);
                    if (tile.KeepAspect)
                        area = TileProportioned(area, assets.Texture(tile.Asset));
                    var mesh = tile.Shape switch
                    {
                        TileShape.Hex => MeshFactory.ImageHexPrism(area.y * 0.6f, thickness),
                        TileShape.Circle => MeshFactory.ImageCylinder(area.y / 2f, thickness),
                        _ => MeshFactory.ImageBox(new Vector3(area.x, thickness, area.y)),
                    };
                    var face = assets.ImageMaterial(tile.Asset, model.Tint.ToColor(new Color(0.8f, 0.7f, 0.5f)));
                    var edge = model.Tint.ToColor(new Color(0.6f, 0.5f, 0.35f));
                    SetPrism(view, mesh, face, face, tile.Count > 1 ? assets.Grooved(edge) : assets.Plain(edge), 0.005f * tile.Count);
                    SetCell(view, assets, MeshFactory.SideSubmesh, new Vector4(1f, tile.Count, 0f, 0f));
                    break;
                }
                case Die die:
                    RefreshDie(view, assets, die, tint);
                    break;
                case Bag bundleBag when bundleBag.Asset != null && assets.Bundle(bundleBag.Asset) is { } bagPrefab:
                    // A bag that is a TTS asset bundle (Custom_Assetbundle_Bag): container and model at once.
                    SetBundle(view, bagPrefab, 0.05f);
                    break;
                case Bag bag when bag.Asset != null && assets.Mesh(bag.Asset) is { } bagMesh:
                    // A bag with its own model (TTS Custom_Model_Bag): the model's mesh with its image, tinted as TTS does.
                    SetModel(view, bagMesh, bag.DiffuseAsset != null
                        ? assets.TintedImageMaterial(bag.DiffuseAsset, model.Tint.ToColor(Color.white))
                        : assets.Plain(model.Tint.ToColor(bag is InfiniteBag ? InfiniteBagCloth : BagCloth), 0.1f), 0.05f);
                    break;
                case Bag bag:
                {
                    // TTS's built-in bag: a cloth pouch tied at the neck.
                    var cloth = assets.Plain(model.Tint.ToColor(bag is InfiniteBag ? InfiniteBagCloth : BagCloth), 0.1f);
                    SetModel(view, MeshFactory.Pouch(BagRadius, BagHeight), cloth, 0.05f);
                    break;
                }
                case Model bundled when assets.Bundle(bundled.Asset) is { } prefab:
                    // A TTS asset bundle (Custom_Assetbundle, M4): the bundle's own model, materials and colliders.
                    SetBundle(view, prefab, 0.05f);
                    break;
                case Model bowl when assets.Find(bowl.Asset)?.Path == Asset.BuiltinPrefix + "bowl" && assets.Mesh(bowl.Asset) is { } bowlMesh:
                    SetModel(view, bowlMesh, assets.Plain(model.Tint.ToColor(Ivory), 0.4f), 0.15f);
                    BowlColliders(view);
                    break;
                case Model modelObject when assets.Mesh(modelObject.Asset) is { } modelMesh:
                {
                    // A custom model (TTS Custom_Model, G38): its OBJ mesh with its diffuse image.
                    var skin = modelObject.DiffuseAsset != null
                        ? assets.TintedImageMaterial(modelObject.DiffuseAsset, model.Tint.ToColor(Color.white))
                        : assets.Plain(model.Tint.ToColor(Ivory), modelObject.Material == ModelMaterial.Metal ? 0.7f : 0.3f);
                    SetModel(view, modelMesh, skin, 0.05f);
                    break;
                }
                case Board board:
                {
                    var size = board.Size?.ToUnity() ?? BoardDefaultSize;
                    var face = assets.ImageMaterial(board.Asset, model.Tint.ToColor(new Color(0.85f, 0.8f, 0.7f)));
                    SetPrism(view, MeshFactory.ImageBox(new Vector3(size.x, BoardThickness, size.y)), face, assets.Plain(new Color(0.3f, 0.25f, 0.2f)),
                        assets.Plain(new Color(0.3f, 0.25f, 0.2f)), 0.2f);
                    break;
                }
                case Pdf pdf:
                {
                    // A sheet of paper showing its page on top (M7 T8.8); the page is stretched to the sheet, as in TTS.
                    var size = (pdf.Size ?? Pdf.DefaultSize).ToUnity();
                    var paper = new Color(0.95f, 0.94f, 0.9f);
                    SetPrism(view, MeshFactory.ImageBox(new Vector3(size.x, PdfThickness, size.y)), assets.PdfMaterial(pdf.Asset, pdf.Page, paper),
                        assets.Plain(paper, 0.1f), assets.Plain(paper * 0.9f, 0.1f), 0.05f);
                    break;
                }
                case Counter _:
                case Clock _:
                {
                    // A block with a glowing display on top (G27).
                    var size = model is Counter ? CounterSize : ClockSize;
                    var readout = view.GetComponent<Readout>() ?? view.gameObject.AddComponent<Readout>();
                    readout.Init(view);
                    var case_ = assets.Plain(model.Tint.ToColor(DisplayCase), 0.5f);
                    SetPrism(view, MeshFactory.Box(size), readout.Material, case_, case_, 0.06f);
                    break;
                }
                case Notecard _:
                {
                    // Paper with its title and text printed on top (G27).
                    var size = Notecard.Size.ToUnity();
                    const float thickness = 0.0015f;
                    var paper = assets.Plain(model.Tint.ToColor(new Color(0.98f, 0.96f, 0.9f)), 0.1f);
                    SetPrism(view, MeshFactory.Box(new Vector3(size.x, thickness, size.y)), paper, paper, paper, 0.004f);
                    NotecardView.Refresh(view, size, thickness);
                    break;
                }
                case Figure figure:
                {
                    var height = figure.Height ?? FigureDefaultHeight;
                    var face = assets.ImageMaterial(figure.Asset, model.Tint.ToColor(Ivory));
                    SetPrism(view, MeshFactory.Box(new Vector3(FigureWidth, FigureThickness, height)), face, face, assets.Plain(Ivory), 0.005f);
                    EnsureStand(view, height, assets.Plain(new Color(0.15f, 0.15f, 0.17f)));
                    view.Body.centerOfMass = new Vector3(0f, -height * 0.4f, 0f);
                    break;
                }
                default:
                {
                    var grey = assets.Plain(model.Tint.ToColor(Placeholder));
                    // A stand-in stays a small box in the world whatever the object's scale (a TTS mat at scale 45 is not a 1.4 m cube).
                    var unscale = 1f / Mathf.Max(0.01f, Mathf.Abs(model.Transform.Scale.X));
                    SetPrism(view, MeshFactory.Box(Vector3.one * ModelPlaceholderSize * unscale), grey, grey, grey, 0.02f);
                    break;
                }
            }
            view.Body.isKinematic = view.Pinned || !view.Renderer.enabled || view.Held || view.Animating;
            // A script hid the shape (the object holds UI), or its tint is fully transparent (a TTS click or drop area):
            // nothing is drawn, the rest stays. A partly transparent tint draws the piece see-through.
            view.Renderer.forceRenderingOff = view.ShapeHidden || view.ZoneHidden;
            if (model.Tint is { Length: > 3 } tinted && tinted[3] > ObjectView.InvisibleAlpha && tinted[3] < 0.999f)
            {
                var materials = view.Renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = assets.Translucent(materials[i], tinted[3]);
                view.Renderer.sharedMaterials = materials;
            }
            ScriptButtonsView.Refresh(view);
            ScriptInputsView.Refresh(view);
        }

        /// <summary>
        /// A TTS tile's size for its image: the depth stays and the width follows the image's proportions, wide or tall
        /// (TTS rule; portrait panels then sit edge to edge with their neighbours as in TTS). Until the image is here the
        /// tile stays square; it is reshaped when the image arrives.
        /// </summary>
        private static Vector2 TileProportioned(Vector2 area, Texture texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return area;
            return new Vector2(area.y * texture.width / texture.height, area.y);
        }

        /// <summary>
        /// A base size given the image's proportions: the longer side grows, the shorter keeps the base (TTS image
        /// tokens). Until the image is here the piece stays square; it is reshaped when the image arrives.
        /// </summary>
        private static Vector2 Proportioned(Vector2 area, Texture texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return area;
            var aspect = (float)texture.width / texture.height;
            return aspect >= 1f ? new Vector2(area.y * aspect, area.y) : new Vector2(area.x, area.x / aspect);
        }

        private static void RefreshCard(ObjectView view, AssetLibrary assets, Card card)
        {
            var size = (card.Size ?? Card.DefaultSize).ToUnity();
            var mesh = MeshFactory.ImageBox(new Vector3(size.x, CardThickness, size.y));
            // A card in another player's hand shows its back on both sides.
            var faceAsset = view.HideFace ? card.BackAsset : card.FaceAsset;
            var faceIndex = view.HideFace ? card.BackIndex : card.FaceIndex;
            SetPrism(view, mesh, assets.ImageMaterial(faceAsset, Ivory), assets.ImageMaterial(card.BackAsset, Ivory), assets.Plain(CardEdge), CardMass);
            SetCell(view, assets, MeshFactory.TopSubmesh, assets.CellScaleOffset(faceAsset, faceIndex));
            UseCardPhysics(view);
            SetCell(view, assets, MeshFactory.BottomSubmesh, assets.CellScaleOffset(card.BackAsset, card.BackIndex));
        }

        /// <summary>
        /// The side facing up shows the top card (<c>cards[0]</c>), the side facing down the bottom card. The mesh's +Y side
        /// always shows a face and its −Y side a back, so which card each side shows depends on the deck's orientation.
        /// </summary>
        private static void RefreshDeck(ObjectView view, AssetLibrary assets, Deck deck)
        {
            var size = (deck.Size ?? Card.DefaultSize).ToUnity();
            var count = deck.Cards.Count;
            var mesh = MeshFactory.ImageBox(new Vector3(size.x, CardThickness * Mathf.Max(1, count), size.y));
            if (count == 0)
            {
                // An empty deck stays as a drop target: invisible, kinematic, its collider a trigger.
                SetPrism(view, mesh, assets.Plain(Ivory), assets.Plain(Ivory), assets.Plain(Ivory), CardMass);
                UseCardPhysics(view);
                view.Renderer.enabled = false;
                view.Collider.isTrigger = true;
                return;
            }

            var faceSideCard = deck.IsFaceUp ? deck.Cards[0] : deck.Cards[count - 1];
            var backSideCard = deck.IsFaceUp ? deck.Cards[count - 1] : deck.Cards[0];
            var faceAsset = faceSideCard.FaceAsset ?? deck.FaceAsset;
            var backAsset = backSideCard.BackAsset ?? deck.BackAsset;
            var backIndex = backSideCard.BackIndex ?? deck.BackIndex;
            SetPrism(view, mesh, assets.ImageMaterial(faceAsset, Ivory), assets.ImageMaterial(backAsset, Ivory), assets.Plain(CardEdge), CardMass * count);
            UseCardPhysics(view);
            view.Renderer.enabled = true;
            view.Collider.isTrigger = false;
            SetCell(view, assets, MeshFactory.TopSubmesh, assets.CellScaleOffset(faceAsset, faceSideCard.FaceIndex));
            SetCell(view, assets, MeshFactory.BottomSubmesh, assets.CellScaleOffset(backAsset, backIndex));
        }

        private static void RefreshDie(ObjectView view, AssetLibrary assets, Die die, Color tint)
        {
            var geometry = DiceGeometry.Get(die.Faces);
            // A custom d6 image follows the TTS template (G21); other custom dice use the builtin atlas layout.
            view.Filter.sharedMesh = die.Asset != null && geometry.TemplateMesh != null ? geometry.TemplateMesh : geometry.Mesh;
            var material = die.Asset != null
                ? assets.ImageMaterial(die.Asset, tint)
                : assets.ImageMaterial(BuiltinDiceAsset(assets), tint);
            view.Renderer.sharedMaterials = new[] { material };
            if (!(view.Collider is MeshCollider collider))
            {
                if (view.Collider != null)
                    Object.Destroy(view.Collider);
                collider = view.gameObject.AddComponent<MeshCollider>();
                view.Collider = collider;
            }
            collider.convex = true;
            collider.sharedMesh = geometry.Mesh;
            collider.sharedMaterial = DiceMaterial;
            view.Body.mass = 0.004f;
            // Thrown dice spin fast and are small: continuous collision so they never tunnel, and the default
            // 7 rad/s spin cap lifted so a shake carries into the throw.
            view.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            view.Body.maxAngularVelocity = 50f;
            view.Body.angularDamping = 0.2f;
        }

        /// <summary>Dice use <c>builtin:dice_numbers</c> even when the table does not list it.</summary>
        private static string BuiltinDiceAsset(AssetLibrary assets)
        {
            const string id = "__builtin_dice_numbers";
            if (assets.Find(id) == null)
                assets.Register(new Asset { Id = id, Type = AssetType.Image, Path = "builtin:dice_numbers", Columns = 5, Rows = 4 });
            return id;
        }

        private static void UseCardPhysics(ObjectView view)
        {
            view.Collider.sharedMaterial = CardMaterial;
            view.Body.linearDamping = CardLinearDamping;
            view.Body.angularDamping = CardAngularDamping;
        }

        private static void SetPrism(ObjectView view, Mesh mesh, Material top, Material bottom, Material sides, float mass)
        {
            view.Filter.sharedMesh = mesh;
            view.Renderer.sharedMaterials = new[] { top, bottom, sides };
            view.Body.mass = mass;

            // Box colliders for boxes (cheap and stable for thin cards), convex mesh colliders for everything else.
            var isBox = mesh.name.StartsWith("box:") || mesh.name.StartsWith("ibox:");
            var bounds = mesh.bounds;
            if (isBox)
            {
                if (!(view.Collider is BoxCollider box))
                {
                    if (view.Collider != null)
                        Object.Destroy(view.Collider);
                    box = view.gameObject.AddComponent<BoxCollider>();
                    view.Collider = box;
                }
                var center = view.Visual == view.transform ? bounds.center : view.Visual.localRotation * bounds.center;
                var size = view.Visual == view.transform ? bounds.size : Abs(view.Visual.localRotation * bounds.size);
                // Writing the same box again still rebuilds the shape in PhysX and drops its contacts: only on a change.
                if (box.center != center)
                    box.center = center;
                if (box.size != size)
                    box.size = size;
            }
            else
            {
                if (!(view.Collider is MeshCollider meshCollider))
                {
                    if (view.Collider != null)
                        Object.Destroy(view.Collider);
                    meshCollider = view.gameObject.AddComponent<MeshCollider>();
                    view.Collider = meshCollider;
                }
                meshCollider.convex = true;
                if (meshCollider.sharedMesh != mesh)
                    meshCollider.sharedMesh = mesh;
            }
        }

        /// <summary>
        /// A bowl holds pieces in its hollow: its own collider is a thin base, and a ring of wall boxes stands around it
        /// (child colliders of the same body, made once). A convex hull would fill the bowl and push pieces out.
        /// </summary>
        private static void BowlColliders(ObjectView view)
        {
            const int walls = 16;
            if (view.Collider is MeshCollider collider)
                collider.sharedMesh = MeshFactory.Cylinder(Builtin.BuiltinModels.BowlRadius * 0.6f, Builtin.BuiltinModels.BowlWall * 2f, 24);
            if (view.transform.Find("Bowl walls") != null)
                return;
            var ring = new GameObject("Bowl walls").transform;
            ring.SetParent(view.transform, false);
            const float r = Builtin.BuiltinModels.BowlRadius, h = Builtin.BuiltinModels.BowlHeight;
            for (var i = 0; i < walls; i++)
            {
                var angle = i * 360f / walls;
                var wall = new GameObject($"Wall {i}");
                wall.transform.SetParent(ring, false);
                // Leaning out like the bowl's side, from its floor's edge to its rim.
                var mid = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 0.8f * r);
                wall.transform.localPosition = new Vector3(mid.x, h * 0.5f, mid.z);
                wall.transform.localRotation = Quaternion.Euler(-28f, angle, 0f);
                var box = wall.AddComponent<BoxCollider>();
                box.size = new Vector3(2f * Mathf.PI * r / walls * 1.1f, h * 1.15f, Builtin.BuiltinModels.BowlWall * 2f);
                box.sharedMaterial = view.Collider != null ? view.Collider.sharedMaterial : null;
            }
        }

        /// <summary>A model's own mesh (one material) with a convex collider of its shape.</summary>
        private static void SetModel(ObjectView view, Mesh mesh, Material material, float mass)
        {
            view.Filter.sharedMesh = mesh;
            view.Renderer.sharedMaterials = new[] { material };
            view.Body.mass = mass;
            if (!(view.Collider is MeshCollider collider))
            {
                if (view.Collider != null)
                    Object.Destroy(view.Collider);
                collider = view.gameObject.AddComponent<MeshCollider>();
                view.Collider = collider;
            }
            collider.convex = true;
            collider.sharedMesh = mesh;
        }

        /// <summary>
        /// Shows an asset bundle's prefab on the object (once; refreshes keep it): the object's own mesh is emptied, and
        /// the bundle's colliders become the object's (a box around it when it has none).
        /// </summary>
        private static void SetBundle(ObjectView view, GameObject prefab, float mass)
        {
            view.Filter.sharedMesh = null;
            view.Body.mass = mass;
            if (view.transform.Find("Bundle") != null)
                return;
            var copy = Bundles.Attach(view.transform, prefab);
            if (view.Collider != null && view.Collider.transform == view.transform)
                Object.Destroy(view.Collider);
            var colliders = copy.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                view.Collider = colliders[0];
                return;
            }
            var bounds = new Bounds(view.transform.position, Vector3.zero);
            var first = true;
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (first)
                    bounds = renderer.bounds;
                else
                    bounds.Encapsulate(renderer.bounds);
                first = false;
            }
            var box = view.gameObject.AddComponent<BoxCollider>();
            box.center = view.transform.InverseTransformPoint(bounds.center);
            var size = view.transform.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.005f), Mathf.Max(Mathf.Abs(size.y), 0.005f), Mathf.Max(Mathf.Abs(size.z), 0.005f));
            view.Collider = box;
        }

        /// <summary>A round stand under a standee: its own renderer and convex collider, part of the object's rigidbody.</summary>
        private static void EnsureStand(ObjectView view, float height, Material material)
        {
            const float radius = 0.016f;
            const float thickness = 0.003f;
            if (view.Base == null)
            {
                var stand = new GameObject("Stand");
                stand.transform.SetParent(view.transform, false);
                stand.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Cylinder(radius, thickness, 24);
                view.Base = stand.AddComponent<MeshRenderer>();
                var collider = stand.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMesh = MeshFactory.Cylinder(radius, thickness, 24);
            }
            view.Base.transform.localPosition = new Vector3(0f, -height / 2f + thickness / 2f, 0f);
            view.Base.sharedMaterials = new[] { material, material, material };
        }

        /// <summary>Shows one cell of the submesh's image, through a material shared by every piece showing that cell (<see cref="AssetLibrary.Cell"/>).</summary>
        private static void SetCell(ObjectView view, AssetLibrary assets, int submesh, Vector4 scaleOffset)
        {
            var materials = view.Renderer.sharedMaterials;
            if (submesh >= materials.Length)
                return;
            materials[submesh] = assets.Cell(materials[submesh], scaleOffset);
            view.Renderer.sharedMaterials = materials;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
