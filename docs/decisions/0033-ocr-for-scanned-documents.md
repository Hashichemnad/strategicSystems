# 0033 — Text recognition (OCR) for scanned PDFs and images with Tesseract

- **Status:** Accepted by the owner (Phase 5) — including the LGPL OS libraries used only by the `tesseract` program · `libjbig0` (GPL-2+)
  **accepted by the owner in Phase 18** (option a), see "Licences"
- **Context:** Document-to-form extraction read only documents with a text layer (ADR 0030); scanned PDFs and photographed pages answered
  "no text". The owner asked for optional OCR with Tesseract (Apache-2.0), Arabic and English, as a sidecar or a bundled engine.

## Decision
- **Bundled engine, separate process.** The `tesseract` command-line program (5.x) with the `ara` and `eng` language data is installed in the
  application image (`deploy/Dockerfile`, build argument `INSTALL_OCR=true` by default) and run per page:
  `tesseract <image> stdout -l ara+eng --psm 3`. No native interop (a crash or a hang only fails that page; time-out `Ocr:TimeoutSeconds`,
  default 60; `OMP_THREAD_LIMIT=1`); no second container to deploy, secure and monitor; installations without the package simply report OCR
  as unavailable. `Ocr:TesseractPath` points to another binary (Windows / non-Docker installs). Behind `IOcrEngine`, so a sidecar can replace
  it later without touching the extractor.
- **What is recognised.**
  - **Images**: PNG, JPEG and TIFF uploads (recognised by signature, ≤ 10 MB) → `[image scan]`.
  - **PDF pages without a usable text layer** (fewer than 10 letters): the page is **rendered to a greyscale PNG with PDFium** and the
    rendering recognised → `[page n scan]`. PDFium (BSD-3-Clause; Google's PDF engine) decodes every PDF image codec — JPEG, JPEG 2000, Flate,
    **CCITT G3 / G4 and JBIG2** (its own BSD decoder) — so scans from black-and-white office scanners are read too. It is used through
    **PDFtoImage** (MIT), which brings the `bblanchon.PDFium.*` binaries (Apache-2.0) and SkiaSharp (MIT). No Poppler, Ghostscript or MuPDF
    (GPL / AGPL). Pages that keep their text layer are read as before.
  - **Render resolution = the scan's own resolution**: pixels of the page's largest image over the inches it covers, clamped to 150–400 dpi
    (300 dpi for pages without an image). Enlarging a 150 dpi scan to 300 dpi made Tesseract's line finder drop lines in testing; rendering at
    the scanner's resolution reproduces the original pixels.
  - At most **`MaxPages`** scanned pages per document (default 20).
- **Settings → AI → Data sharing and limits → Text recognition (OCR)**: on / off (**off by default**), languages (`ara+eng`), page limit; the
  page shows whether Tesseract and the languages are installed on the server. With OCR off, an image answers `ai.extract.ocrOff` and a scanned
  PDF `ai.extract.noText`, both explaining where to switch it on; no model is called.
- **"From scan" marking.** Each proposed value carries its location (`page` / `slide` / `image` + number) and `fromScan` when the quoted source
  lies in recognised text; the review UI shows a **"From scan"** chip ("check spelling and numbers"), the result counts the scanned pages, and
  the audit event of the extraction records `scannedPages`. OCR output is cleaned like other text (NFKC, control and **bidi marks** removed);
  Tesseract already returns Arabic in reading order.
- **Tests** run the real engine on committed samples (`tests/Strategy.UnitTests/AI/Samples`): an Arabic page and an English page rendered with
  the product's fonts and degraded like office scans (grey, 0.8° rotation, noise, blur; Arabic as JPEG, English as PNG and TIFF), as images
  and wrapped in image-only PDFs; and two **bilevel black-and-white scans as the only content of a PDF page**: the Arabic page as **CCITT
  Group 4** (`scan-ar-ccitt-g4.pdf`) and the English page as **JBIG2** (`scan-en-jbig2.pdf`, which PdfPig alone cannot decode), rendered by
  PDFium at their 150 dpi and recognised. The fixtures were generated with Pillow/libtiff (G4) and the `jbig2enc` encoder (test-time tooling,
  not shipped). CI installs `tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng`.

## Licences
- Tesseract and its language data: **Apache-2.0**; Leptonica: **BSD-2-Clause**; PDFium: **BSD-3-Clause** (binaries Apache-2.0, bundled
  third-party code FreeType, libjpeg-turbo, OpenJPEG, lcms, ICU, zlib, libpng, libtiff, AGG, Abseil — all permissive); PDFtoImage and
  SkiaSharp: **MIT** — within CLAUDE.md §4.3.
- **Accepted by the owner:** the **LGPL** OS libraries Tesseract's package brings (glib, cairo, pango, fribidi, libthai, libdatrie,
  graphite2, keyutils, libssh, librtmp), used only as shared libraries by the separate `tesseract` program, like glibc in the base image.
- **Notices ship with the product:** the image build copies the Debian copyright file of every package the OCR install adds into
  `/app/licenses/ocr/` with `packages.txt` (name, version) and **fails if one is missing**; PDFium's and SkiaSharp's notices are in the app's
  `licenses/` folder (from `src/Host/Strategy.Host/licenses/`). All these OS packages are listed in `docs/THIRD_PARTY_LICENSES.md`.
- **Decided (Phase 18, owner): keep `libjbig0`.** `libjbig0` (JBIG-KIT 2.1) is **GPL-2+ only** (not LGPL). It is a hard dependency of the distribution's
  `libtiff6`, which Leptonica / Tesseract load to read TIFF images; like the LGPL libraries it is an unmodified OS shared library used only by
  the separate `tesseract` program, and its notice ships in `/app/licenses/ocr/`. GPL distribution obligations (source availability) would
  apply to it as part of the image. Options: (a) accept it on the same footing as the LGPL libraries; (b) ship the default image with
  `INSTALL_OCR=false` and offer OCR as an add-on image / sidecar; (c) later, a Tesseract build against a libtiff without JBIG support. Until
  the owner decided, `INSTALL_OCR=false` was documented as the way to build an image without it. (PDFium's own JBIG2 decoder is BSD and
  unrelated.)
  **Decision (Phase 18):** option (a). The image build writes `/app/licenses/ocr/NOTICE-libjbig0-GPL-2.txt` (licence, package and Ubuntu
  source-package version, Launchpad source URL and `apt-get source` command), `GPL-2.txt` (full text) and `SOURCES.txt` (source package and
  source URL of every OCR package), next to `libjbig0.copyright`; the build fails if the GPL-2 text is missing. `INSTALL_OCR=false` still
  builds an image without the OCR packages and without `libjbig0` (installation guide §2).

## Consequences
- The application image grows by roughly 40 MB with OCR, plus PDFium and Skia native libraries (about 10 MB per platform) in the app. OCR is CPU-bound (about 1–3 s per page on one core); the page limit and the per-page
  time-out bound one upload.
- Recognition quality depends on the scan; values from scans are always reviewed (the review UI marks them, CLAUDE.md §2 rule 6).
