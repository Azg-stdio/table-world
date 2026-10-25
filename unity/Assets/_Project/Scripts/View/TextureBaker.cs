using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TableWorld.View
{
    /// <summary>
    /// Makes decoded images GPU-ready off the main thread: at most <see cref="AssetLibrary.MaxTextureSize"/> on the longer
    /// side, sides a multiple of 4, mipmaps, and BC1 when opaque (an eighth of the memory) or BC3 when see-through (a
    /// quarter), all in Burst jobs (TextureJobs.cs). The main thread only starts the jobs and, a few per frame
    /// within <see cref="UploadBudgetMs"/>, hands the finished blocks to the GPU, so a table keeps its frame rate while its
    /// images arrive.
    /// </summary>
    public sealed class TextureBaker : MonoBehaviour
    {
        /// <summary>Main-thread time per frame for uploading finished textures; at least one goes each frame.</summary>
        public const float UploadBudgetMs = 4f;

        private sealed class Bake
        {
            public Texture2D Source;
            public string Name;
            public int AnisoLevel;
            public Action<Texture2D> Done;
            public int Width, Height;
            public NativeArray<byte> RowHasAlpha;
            public NativeArray<byte>[] Levels;
            public NativeArray<byte>[] Blocks;
            public bool Alpha;
            public JobHandle Handle;
            public bool Compressing;
        }

        private static TextureBaker _instance;
        private readonly List<Bake> _bakes = new List<Bake>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Images being baked now.</summary>
        public static int Pending => _instance != null ? _instance._bakes.Count : 0;

        /// <summary>
        /// Bakes <paramref name="source"/> (a readable, freshly decoded image; destroyed here) and calls
        /// <paramref name="done"/> with the GPU texture on a later frame. Formats the jobs do not read go through
        /// <see cref="AssetLibrary.Fit"/> at once.
        /// </summary>
        public static void Start(Texture2D source, Action<Texture2D> done)
        {
            int channels, r, g, b, a;
            switch (source.format)
            {
                case TextureFormat.RGBA32: channels = 4; r = 0; g = 1; b = 2; a = 3; break;
                case TextureFormat.ARGB32: channels = 4; a = 0; r = 1; g = 2; b = 3; break;
                case TextureFormat.RGB24: channels = 3; r = 0; g = 1; b = 2; a = -1; break;
                default:
                    done(AssetLibrary.Fit(source));
                    return;
            }
            if (_instance == null)
            {
                var host = new GameObject("Texture baker") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<TextureBaker>();
            }

            var largest = Mathf.Max(source.width, source.height);
            var scale = largest > AssetLibrary.MaxTextureSize ? (float)AssetLibrary.MaxTextureSize / largest : 1f;
            // Block compression works on 4 × 4 blocks: round the sides (a sprite sheet's grid scales with them).
            var width = Mathf.Max(4, Mathf.RoundToInt(source.width * scale / 4f) * 4);
            var height = Mathf.Max(4, Mathf.RoundToInt(source.height * scale / 4f) * 4);
            var bake = new Bake
            {
                Source = source, Name = source.name, AnisoLevel = source.anisoLevel, Done = done, Width = width, Height = height,
                RowHasAlpha = new NativeArray<byte>(height, Allocator.Persistent),
                Levels = new NativeArray<byte>[MipCount(width, height)],
            };
            bake.Levels[0] = new NativeArray<byte>(width * height * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            bake.Handle = new ResampleJob
            {
                Source = source.GetRawTextureData<byte>(), SourceWidth = source.width, SourceHeight = source.height, Channels = channels,
                R = r, G = g, B = b, A = a, Width = width, Height = height, Target = bake.Levels[0], RowHasAlpha = bake.RowHasAlpha,
            }.Schedule(height, 16);
            JobHandle.ScheduleBatchedJobs();
            _instance._bakes.Add(bake);
        }

        private static int MipCount(int width, int height)
        {
            var count = 1;
            for (var side = Mathf.Max(width, height); side > 1; side >>= 1)
                count++;
            return count;
        }

        private void Update()
        {
            var clock = Stopwatch.StartNew();
            var uploaded = false;
            for (var i = 0; i < _bakes.Count; i++)
            {
                var bake = _bakes[i];
                if (!bake.Handle.IsCompleted)
                    continue;
                bake.Handle.Complete();
                if (!bake.Compressing)
                {
                    StartCompressing(bake);
                    continue;
                }
                // Uploads are the main thread's share: keep them within the budget, but always let one through.
                if (uploaded && clock.Elapsed.TotalMilliseconds > UploadBudgetMs)
                    break;
                _bakes.RemoveAt(i--);
                uploaded = true;
                Finish(bake);
            }
        }

        /// <summary>The pixels are in: the source can go, and mipmaps and blocks are made, one level after the other.</summary>
        private static void StartCompressing(Bake bake)
        {
            Object.Destroy(bake.Source);
            bake.Source = null;
            for (var y = 0; y < bake.Height && !bake.Alpha; y++)
                bake.Alpha = bake.RowHasAlpha[y] != 0;
            bake.RowHasAlpha.Dispose();
            bake.Blocks = new NativeArray<byte>[bake.Levels.Length];
            var bytesPerBlock = bake.Alpha ? 16 : 8;
            var previous = default(JobHandle);
            var all = default(JobHandle);
            int width = bake.Width, height = bake.Height;
            for (var level = 0; level < bake.Levels.Length; level++)
            {
                if (level > 0)
                {
                    int w = Mathf.Max(1, width >> 1), h = Mathf.Max(1, height >> 1);
                    bake.Levels[level] = new NativeArray<byte>(w * h * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    previous = new DownsampleJob
                    {
                        Source = bake.Levels[level - 1], SourceWidth = width, SourceHeight = height, Width = w, Height = h, Target = bake.Levels[level],
                    }.Schedule(h, 32, previous);
                    width = w;
                    height = h;
                }
                var blocks = ((width + 3) / 4) * ((height + 3) / 4);
                bake.Blocks[level] = new NativeArray<byte>(blocks * bytesPerBlock, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var compress = new CompressJob { Pixels = bake.Levels[level], Width = width, Height = height, Alpha = bake.Alpha, Output = bake.Blocks[level] }
                    .Schedule(blocks, 64, previous);
                all = JobHandle.CombineDependencies(all, compress);
            }
            bake.Handle = JobHandle.CombineDependencies(all, previous);
            bake.Compressing = true;
            JobHandle.ScheduleBatchedJobs();
        }

        private static void Finish(Bake bake)
        {
            var texture = new Texture2D(bake.Width, bake.Height, bake.Alpha ? TextureFormat.DXT5 : TextureFormat.DXT1, bake.Levels.Length, linear: false)
            {
                name = bake.Name, anisoLevel = bake.AnisoLevel,
            };
            for (var level = 0; level < bake.Blocks.Length; level++)
                texture.SetPixelData(bake.Blocks[level], level);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Release(bake);
            bake.Done(texture);
        }

        private static void Release(Bake bake)
        {
            if (bake.RowHasAlpha.IsCreated)
                bake.RowHasAlpha.Dispose();
            foreach (var level in bake.Levels)
                if (level.IsCreated)
                    level.Dispose();
            if (bake.Blocks != null)
                foreach (var blocks in bake.Blocks)
                    if (blocks.IsCreated)
                        blocks.Dispose();
            if (bake.Source != null)
                Object.Destroy(bake.Source);
        }

        private void OnDestroy()
        {
            foreach (var bake in _bakes)
            {
                bake.Handle.Complete();
                Release(bake);
            }
            _bakes.Clear();
            if (_instance == this)
                _instance = null;
        }
    }
}
