# Licence notices shipped with the application

- `pdfium/` — PDFium (BSD-3-Clause / Apache-2.0) and the third-party code compiled into `libpdfium` (from the pdfium-binaries release
  `chromium/7961`, the same binary as the `bblanchon.PDFium.*` 152.0.7961 NuGet packages). Used to render scanned PDF pages for OCR (ADR 0033).
- `skiasharp/` — SkiaSharp native assets (MIT) and their third-party notices, used by PDFtoImage.
- `ocr/` (in the container image only) — the Debian copyright file of every package installed with Tesseract, and `packages.txt` with their
  versions (`deploy/Dockerfile`, ADR 0033).

All .NET and npm dependencies are listed in `docs/THIRD_PARTY_LICENSES.md`.
