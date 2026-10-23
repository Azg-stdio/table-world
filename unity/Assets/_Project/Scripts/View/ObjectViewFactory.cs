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
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static PhysicsMaterial _diceMaterial;

        /// <summary>Hard plastic on felt: a little bounce, moderate friction, so dice tumble and then settle on a face.</summary>
        private static PhysicsMaterial DiceMaterial => _diceMaterial != null ? _diceMaterial : _diceMaterial = new PhysicsMaterial("Dice")
        {
            bounciness = 0.25f,
            dynamicFriction = 0.35f,
            staticFriction = 0.5f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
        };

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
                    var size = token.Thickness ?? TokenThickness;
                    var mesh = token.Shape == TokenShape.Round
                        ? MeshFactory.Cylinder(TokenDiameter / 2f, size)
                        : MeshFactory.Box(new Vector3(TokenDiameter, size, TokenDiameter));
                    var face = assets.ImageMaterial(token.Asset, model.Tint.ToColor(Ivory));
                    SetPrism(view, mesh, face, face, assets.Plain(model.Tint.ToColor(Ivory) * 0.85f), 0.002f);
                    break;
                }
                case Tile tile:
                {
                    var thickness = tile.Thickness ?? TileThickness;
                    var mesh = tile.Shape switch
                    {
                        TileShape.Hex => MeshFactory.HexPrism(TileSize * 0.6f, thickness),
                        TileShape.Circle => MeshFactory.Cylinder(TileSize / 2f, thickness),
                        _ => MeshFactory.Box(new Vector3(TileSize, thickness, TileSize)),
                    };
                    var face = assets.ImageMaterial(tile.Asset, model.Tint.ToColor(new Color(0.8f, 0.7f, 0.5f)));
                    SetPrism(view, mesh, face, face, assets.Plain(model.Tint.ToColor(new Color(0.6f, 0.5f, 0.35f))), 0.005f);
                    break;
                }
                case Die die:
                    RefreshDie(view, assets, die, tint);
                    break;
                case Bag bag:
                {
                    var cloth = assets.Plain(model.Tint.ToColor(bag is InfiniteBag ? InfiniteBagCloth : BagCloth), 0.1f);
                    SetPrism(view, MeshFactory.Cylinder(BagRadius, BagHeight, 24), cloth, cloth, cloth, 0.05f);
                    break;
                }
                case Board board:
                {
                    var size = board.Size?.ToUnity() ?? BoardDefaultSize;
                    var face = assets.ImageMaterial(board.Asset, model.Tint.ToColor(new Color(0.85f, 0.8f, 0.7f)));
                    SetPrism(view, MeshFactory.Box(new Vector3(size.x, BoardThickness, size.y)), face, assets.Plain(new Color(0.3f, 0.25f, 0.2f)),
                        assets.Plain(new Color(0.3f, 0.25f, 0.2f)), 0.2f);
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
                    SetPrism(view, MeshFactory.Box(Vector3.one * ModelPlaceholderSize), grey, grey, grey, 0.02f);
                    break;
                }
            }
            view.Body.isKinematic = model.Locked || !view.Renderer.enabled || view.Held || view.Animating;
        }

        private static void RefreshCard(ObjectView view, AssetLibrary assets, Card card)
        {
            var size = (card.Size ?? Card.DefaultSize).ToUnity();
            var mesh = MeshFactory.Box(new Vector3(size.x, CardThickness, size.y));
            SetPrism(view, mesh, assets.ImageMaterial(card.FaceAsset, Ivory), assets.ImageMaterial(card.BackAsset, Ivory), assets.Plain(CardEdge), CardMass);
            SetCell(view, MeshFactory.TopSubmesh, assets.CellScaleOffset(card.FaceAsset, card.FaceIndex));
            SetCell(view, MeshFactory.BottomSubmesh, assets.CellScaleOffset(card.BackAsset, card.BackIndex));
        }

        /// <summary>
        /// The side facing up shows the top card (<c>cards[0]</c>), the side facing down the bottom card. The mesh's +Y side
        /// always shows a face and its −Y side a back, so which card each side shows depends on the deck's orientation.
        /// </summary>
        private static void RefreshDeck(ObjectView view, AssetLibrary assets, Deck deck)
        {
            var size = (deck.Size ?? Card.DefaultSize).ToUnity();
            var count = deck.Cards.Count;
            var mesh = MeshFactory.Box(new Vector3(size.x, CardThickness * Mathf.Max(1, count), size.y));
            if (count == 0)
            {
                // An empty deck stays as a drop target: invisible, kinematic, its collider a trigger.
                SetPrism(view, mesh, assets.Plain(Ivory), assets.Plain(Ivory), assets.Plain(Ivory), CardMass);
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
            view.Renderer.enabled = true;
            view.Collider.isTrigger = false;
            SetCell(view, MeshFactory.TopSubmesh, assets.CellScaleOffset(faceAsset, faceSideCard.FaceIndex));
            SetCell(view, MeshFactory.BottomSubmesh, assets.CellScaleOffset(backAsset, backIndex));
        }

        private static void RefreshDie(ObjectView view, AssetLibrary assets, Die die, Color tint)
        {
            var geometry = DiceGeometry.Get(die.Faces);
            view.Filter.sharedMesh = geometry.Mesh;
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

        private static void SetPrism(ObjectView view, Mesh mesh, Material top, Material bottom, Material sides, float mass)
        {
            view.Filter.sharedMesh = mesh;
            view.Renderer.sharedMaterials = new[] { top, bottom, sides };
            view.Body.mass = mass;

            // Box colliders for boxes (cheap and stable for thin cards), convex mesh colliders for everything else.
            var isBox = mesh.name.StartsWith("box:");
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
                box.center = view.Visual == view.transform ? bounds.center : view.Visual.localRotation * bounds.center;
                var size = view.Visual == view.transform ? bounds.size : Abs(view.Visual.localRotation * bounds.size);
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
                meshCollider.sharedMesh = mesh;
            }
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

        private static void SetCell(ObjectView view, int submesh, Vector4 scaleOffset)
        {
            var block = new MaterialPropertyBlock();
            view.Renderer.GetPropertyBlock(block, submesh);
            block.SetVector(BaseMapSt, scaleOffset);
            view.Renderer.SetPropertyBlock(block, submesh);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
