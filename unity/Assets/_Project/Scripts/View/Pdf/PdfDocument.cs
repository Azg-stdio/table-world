using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TableWorld.View.Documents
{
    /// <summary>
    /// One PDF opened with PDFium (M7 T8.8; <c>Assets/Plugins/PDFium</c>, BSD licence, the engine TTS also uses through a
    /// paid wrapper). Pages render to textures on the main thread: PDFium is not thread-safe. The file's bytes stay pinned
    /// while it is open, as PDFium reads them in place.
    /// </summary>
    public sealed class PdfDocument : IDisposable
    {
        private const string Library = "pdfium";
        // FPDF_ANNOT: draw annotations (form fields, stamps) as a viewer would.
        private const int RenderAnnotations = 0x01;
        private const int BitmapBgra = 4;

        [DllImport(Library)] private static extern void FPDF_InitLibrary();
        [DllImport(Library)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string password);
        [DllImport(Library)] private static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Library)] private static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Library)] private static extern uint FPDF_GetLastError();
        [DllImport(Library)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Library)] private static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Library)] private static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Library)] private static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Library)] private static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr buffer, int stride);
        [DllImport(Library)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Library)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        private static bool _initialised;

        private GCHandle _bytes;
        private IntPtr _document;

        public int PageCount { get; }

        private PdfDocument(GCHandle bytes, IntPtr document)
        {
            _bytes = bytes;
            _document = document;
            PageCount = FPDF_GetPageCount(document);
        }

        /// <summary>The document in <paramref name="bytes"/>, or null when PDFium cannot read it (the error is logged).</summary>
        public static PdfDocument Open(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            try
            {
                if (!_initialised)
                {
                    FPDF_InitLibrary();
                    _initialised = true;
                }
                var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                var document = FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), bytes.Length, null);
                if (document == IntPtr.Zero)
                {
                    var error = FPDF_GetLastError();
                    handle.Free();
                    Debug.LogWarning($"[Pdf] '{name}' does not open (PDFium error {error}: {(error == 4 ? "needs a password" : error == 3 ? "not a PDF or damaged" : "unreadable")}).");
                    return null;
                }
                return new PdfDocument(handle, document);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.LogWarning($"[Pdf] PDFium is not available on this platform: {e.Message}");
                return null;
            }
        }

        /// <summary>The page's size in points (1/72 inch), or zero for a page that does not exist.</summary>
        public Vector2 PageSize(int index)
        {
            if (_document == IntPtr.Zero || index < 0 || index >= PageCount)
                return Vector2.zero;
            var page = FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero)
                return Vector2.zero;
            var size = new Vector2(FPDF_GetPageWidthF(page), FPDF_GetPageHeightF(page));
            FPDF_ClosePage(page);
            return size;
        }

        /// <summary>
        /// Page <paramref name="index"/> on white, its longer side <paramref name="pixels"/> long, mipmapped; null when the
        /// page does not exist or does not load.
        /// </summary>
        public Texture2D Render(int index, int pixels)
        {
            if (_document == IntPtr.Zero || index < 0 || index >= PageCount)
                return null;
            var page = FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero)
                return null;
            try
            {
                var w = FPDF_GetPageWidthF(page);
                var h = FPDF_GetPageHeightF(page);
                if (w <= 0f || h <= 0f)
                    return null;
                var scale = pixels / Mathf.Max(w, h);
                var width = Mathf.Max(1, Mathf.RoundToInt(w * scale));
                var height = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                var stride = width * 4;
                var pixelsBuffer = new byte[stride * height];
                var pinned = GCHandle.Alloc(pixelsBuffer, GCHandleType.Pinned);
                try
                {
                    var bitmap = FPDFBitmap_CreateEx(width, height, BitmapBgra, pinned.AddrOfPinnedObject(), stride);
                    FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                    FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, RenderAnnotations);
                    FPDFBitmap_Destroy(bitmap);
                }
                finally
                {
                    pinned.Free();
                }
                // PDFium writes rows from the top; Unity textures start at the bottom.
                var flipped = new byte[pixelsBuffer.Length];
                for (var row = 0; row < height; row++)
                    Buffer.BlockCopy(pixelsBuffer, row * stride, flipped, (height - 1 - row) * stride, stride);
                var texture = new Texture2D(width, height, TextureFormat.BGRA32, true) { name = $"pdf page {index + 1}", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
                // Level 0 only; Apply builds the smaller levels (LoadRawTextureData would want the whole chain).
                texture.SetPixelData(flipped, 0);
                texture.Apply(true, true);
                return texture;
            }
            finally
            {
                FPDF_ClosePage(page);
            }
        }

        public void Dispose()
        {
            if (_document != IntPtr.Zero)
            {
                FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
            }
            if (_bytes.IsAllocated)
                _bytes.Free();
        }
    }
}
