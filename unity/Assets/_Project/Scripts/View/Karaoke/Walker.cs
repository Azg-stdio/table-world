using System;
using System.Collections.Generic;
using Core.Net;
using Core.Rules;
using TableWorld.View.Chalkers;
using TableWorld.View.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// First-person walking in a room people walk in (the karaoke room, ADR 0013 §5, T12.10). WASD (the pan keys) or the
    /// arrows walk, Shift hurries, holding the right mouse button looks around, <see cref="GameAction.Sit"/> (C) sits on
    /// the nearest free sofa place or stands up. The walls and the sofas stop you. The table camera rests meanwhile;
    /// other players see your chalker through <see cref="Body"/>.
    /// </summary>
    public sealed class Walker : MonoBehaviour
    {
        public const float Speed = 1.6f;
        public const float HurrySpeed = 3.2f;
        /// <summary>How wide a walker is, for walls and sofas.</summary>
        public const float Radius = 0.25f;
        /// <summary>How far from a place someone can sit on it.</summary>
        public const float SitReach = 1.3f;
        /// <summary>Degrees the view turns per pixel of mouse movement, before the orbit sensitivity setting.</summary>
        public const float LookPerPixel = 0.15f;

        private Camera _camera;
        private TableCamera _tableCamera;
        private TableInteraction _interaction;
        private EnvironmentView _environment;

        /// <summary>The point between the feet, on the floor.</summary>
        public Vector3 Feet { get; set; }
        /// <summary>Which way the body faces, degrees (0 = +Z).</summary>
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        /// <summary>The sofa place sat on, or null standing.</summary>
        public int? Place { get; private set; }
        public bool Moving { get; private set; }

        /// <summary>Places other people sit on (from their presence): sitting skips them.</summary>
        public Func<IEnumerable<int>> Taken { get; set; }

        /// <summary>Ignore the real keyboard and mouse (tests walk it from code).</summary>
        public bool IgnoreDevices { get; set; }

        /// <summary>A panel over the room has the mouse and keys (the song search): the body stands still and the view stays.</summary>
        public bool Paused { get; set; }

        /// <summary>The body others see, for the presence frame.</summary>
        public AvatarPose Body => new AvatarPose { Position = new Vec3(Feet.x, Feet.y, Feet.z), Yaw = Yaw, Place = Place, Moving = Moving };

        public Lounge Lounge => _environment != null ? _environment.Lounge : null;

        /// <summary>Starts walking with <paramref name="camera"/>, standing at the back of the room facing the screen.</summary>
        public static Walker Begin(Camera camera, EnvironmentView environment, int spot)
        {
            var walker = camera.gameObject.AddComponent<Walker>();
            walker._camera = camera;
            walker._environment = environment;
            walker._tableCamera = camera.GetComponent<TableCamera>();
            if (walker._tableCamera != null)
                walker._tableCamera.enabled = false;
            walker._interaction = FindAnyObjectByType<TableInteraction>();
            var half = EnvironmentView.RoomHalfSize;
            // Spread out along the back of the room, so people joining do not stand inside each other.
            var x = ((spot % 7) - 3) * 0.7f;
            walker.Feet = new Vector3(x, EnvironmentView.FloorY, -half.z + 1.6f);
            walker.Yaw = 0f;
            walker.Pitch = 5f;
            walker.ApplyCamera();
            return walker;
        }

        /// <summary>Stops walking: the table camera takes over again.</summary>
        public void End()
        {
            if (_tableCamera != null)
                _tableCamera.enabled = true;
            Destroy(this);
        }

        private bool _looking;

        private void Update()
        {
            if (Paused)
            {
                Look(false);
                Moving = false;
            }
            else if (!IgnoreDevices)
                ReadDevices(Time.unscaledDeltaTime);
        }

        /// <summary>After everything else moved this frame, so nothing pulls the view back.</summary>
        private void LateUpdate() => ApplyCamera();

        private void OnDisable() => Look(false);

        /// <summary>While looking around the cursor is locked and hidden, so turning never stops at the screen's edge.</summary>
        private void Look(bool on)
        {
            if (on == _looking)
                return;
            _looking = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        private void ReadDevices(float dt)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var typing = _interaction != null && _interaction.KeyboardBlocked != null && _interaction.KeyboardBlocked();
            var looking = mouse != null && mouse.rightButton.isPressed;
            // The frame the cursor locks it jumps to the middle: that jump is not a turn.
            var justLocked = looking && !_looking;
            Look(looking);
            if (looking && !justLocked)
            {
                var delta = mouse.delta.ReadValue();
                var scale = LookPerPixel * (_tableCamera != null ? _tableCamera.OrbitScale : 1f);
                Yaw += delta.x * scale;
                Pitch = Mathf.Clamp(Pitch - delta.y * scale * (_tableCamera != null && _tableCamera.InvertOrbitY ? -1f : 1f), -80f, 80f);
            }
            var move = Vector2.zero;
            if (keyboard != null && !typing && !keyboard.ctrlKey.isPressed)
            {
                if (KeyBindings.Held(keyboard, GameAction.PanForward) || keyboard.upArrowKey.isPressed) move.y += 1;
                if (KeyBindings.Held(keyboard, GameAction.PanBack) || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (KeyBindings.Held(keyboard, GameAction.PanRight) || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (KeyBindings.Held(keyboard, GameAction.PanLeft) || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (KeyBindings.Pressed(keyboard, GameAction.Sit))
                    ToggleSit();
            }
            Walk(move, keyboard != null && keyboard.shiftKey.isPressed, dt);
        }

        /// <summary>
        /// Walks for <paramref name="dt"/> seconds in <paramref name="input"/> (x right, y forward, relative to where the
        /// body faces). Sitting, walking stands you up first.
        /// </summary>
        public void Walk(Vector2 input, bool hurry, float dt)
        {
            Moving = input.sqrMagnitude > 0.01f;
            if (!Moving)
                return;
            if (Place != null)
                StandUp();
            var forward = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
            var step = (forward * input.y + right * input.x).normalized * (hurry ? HurrySpeed : Speed) * dt;
            Feet = Step(Feet, step, Lounge?.Blockers, EnvironmentView.RoomHalfSize);
        }

        /// <summary>
        /// Moves a walker at <paramref name="feet"/> by <paramref name="step"/>: kept inside the walls and out of the
        /// <paramref name="blockers"/>, sliding along what it meets instead of stopping dead.
        /// </summary>
        public static Vector3 Step(Vector3 feet, Vector3 step, IReadOnlyList<Lounge.Footprint> blockers, Vector3 roomHalf)
        {
            var next = feet + new Vector3(step.x, 0f, step.z);
            var limitX = roomHalf.x - Radius - 0.05f;
            var limitZ = roomHalf.z - Radius - 0.05f;
            next.x = Mathf.Clamp(next.x, -limitX, limitX);
            next.z = Mathf.Clamp(next.z, -limitZ, limitZ);
            if (blockers != null)
                // Twice: pushed out of one sofa into another is fixed on the second pass.
                for (var pass = 0; pass < 2; pass++)
                    foreach (var blocker in blockers)
                    {
                        var closest = blocker.Closest(next);
                        var away = new Vector3(next.x - closest.x, 0f, next.z - closest.z);
                        var distance = away.magnitude;
                        if (distance >= Radius)
                            continue;
                        if (distance < 1e-4f)
                        {
                            // Inside it: back out the way the step came in.
                            away = -new Vector3(step.x, 0f, step.z);
                            if (away.sqrMagnitude < 1e-8f)
                                away = blocker.Forward;
                            distance = 0f;
                        }
                        next += away.normalized * (Radius - distance);
                    }
            next.y = feet.y;
            return next;
        }

        /// <summary>Sits on the nearest free sofa place within reach, or stands up when sitting.</summary>
        public bool ToggleSit()
        {
            if (Place != null)
            {
                StandUp();
                return true;
            }
            var lounge = Lounge;
            if (lounge == null)
                return false;
            var taken = new HashSet<int>(Taken?.Invoke() ?? Array.Empty<int>());
            int? best = null;
            var bestDistance = SitReach;
            for (var i = 0; i < lounge.Places.Count; i++)
            {
                if (taken.Contains(i))
                    continue;
                var at = lounge.Places[i].Position;
                var distance = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(Feet.x, Feet.z));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            if (best is not { } place)
                return false;
            Place = place;
            var facing = lounge.Places[place].Facing;
            Yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            Moving = false;
            return true;
        }

        /// <summary>Stands up from the sofa, a step in front of it.</summary>
        public void StandUp()
        {
            if (Place is not { } place || Lounge == null || place >= Lounge.Places.Count)
            {
                Place = null;
                return;
            }
            var (position, facing) = Lounge.Places[place];
            Place = null;
            Feet = Step(new Vector3(position.x, EnvironmentView.FloorY, position.z), facing * 0.55f, Lounge.Blockers, EnvironmentView.RoomHalfSize);
        }

        /// <summary>The eyes: on the sofa at a seated chalker's eye height, standing at a standing one's.</summary>
        public Vector3 Eyes
        {
            get
            {
                if (Place is { } place && Lounge != null && place < Lounge.Places.Count)
                    return Lounge.Places[place].Position + Vector3.up * Chalker.EyeHeight;
                return Chalker.StandingOrigin(Feet) + Vector3.up * Chalker.EyeHeight;
            }
        }

        private void ApplyCamera()
        {
            if (_camera == null)
                return;
            _camera.transform.SetPositionAndRotation(Eyes, Quaternion.Euler(Pitch, Yaw, 0f));
        }
    }
}
