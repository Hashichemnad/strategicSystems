# 0033 — Text recognition (OCR) for scanned PDFs and images with Tesseract

- **Status:** Accepted (Phase 5, added before the pull request at the owner's request) · one licence point for owner confirmation (below)
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
  - **PDF pages without a text layer** (fewer than 10 letters): the page's largest embedded image is recognised — decoded to PNG by PdfPig
    when it can, or the JPEG (`DCTDecode`) stream as is → `[page n scan]`. Pages that keep their text layer are read as before. This needs no
    PDF rasteriser: the free ones (Poppler, Ghostscript, MuPDF) are GPL / AGPL. Scans stored as JBIG2 or CCITT images (some
    black-and-white scanners) cannot be decoded this way and stay unread (reported as "no text").
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
  and wrapped in image-only PDFs. CI installs `tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng`.

## Licences
- Tesseract and its language data: **Apache-2.0**; Leptonica: **BSD-2-Clause** — within CLAUDE.md §4.3.
- **For owner confirmation:** the distribution package also installs operating-system libraries Tesseract links to dynamically, some under
  the **LGPL** (glib, cairo, pango; others MIT / BSD-style: harfbuzz, fontconfig, libarchive, curl, ICU). They are unmodified OS packages used
  as shared libraries by a separate program, like glibc (also LGPL) already in the .NET base image; nothing is linked into the product's code.
  If the owner prefers images without them, build with `INSTALL_OCR=false` (OCR then reports "not installed") or run Tesseract in a
  separate sidecar later behind `IOcrEngine`.

## Consequences
- The application image grows by roughly 40 MB with OCR. OCR is CPU-bound (about 1–3 s per page on one core); the page limit and the per-page
  time-out bound one upload.
- Recognition quality depends on the scan; values from scans are always reviewed (the review UI marks them, CLAUDE.md §2 rule 6).
