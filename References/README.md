# References — فایل‌های مرجع

This folder holds the business reference material the app was designed from. These are inputs, not build inputs:
nothing here is compiled or embedded. The embedded logo lives in
`src/LenjExchange.Infrastructure/Assets/Logo/lenj-logo.png`.

| File | Purpose |
|---|---|
| `Receipt-Template.docx` | LENJ customer payment receipt template (رسید دریافت وجه). Source of the PDF letterhead, colours, section layout, signature block and footer. |
| `Customer-Statement.pdf` | Sample customer statement from a **third-party** exchange. Used only for the statement structure (columns, running balance), not for branding. |
| `BRAND-AND-LAYOUT-SPEC.md` | Extracted company identity, colours, fonts and the measured receipt layout. |
| `extracted/` | Assets pulled from the two files above: logos, LibreOffice renders of the receipt, page renders of the statement. `logo-user-provided.jpg` is the logo supplied separately by the user. |

## Where to put new references — محل قرار دادن فایل‌های جدید

- Updated receipt or statement templates: put them **here**, next to the current file, with a date or version
  suffix (e.g. `Receipt-Template-1405-08.docx`). Do not overwrite the originals.
- A new high-resolution logo (SVG or PNG ≥ 1000 px, transparent background): put it in `extracted/`, then
  replace `src/LenjExchange.Infrastructure/Assets/Logo/lenj-logo.png`. Alternatively, set it at runtime in
  **Settings → Company → Logo** without rebuilding.
- Other business documents (exchanger receipts, agent forms, China receipts): create a subfolder per document type.

فایل‌های قالب جدید را با پسوند تاریخ یا نسخه در همین پوشه قرار دهید و فایل‌های اصلی را جایگزین نکنید.
لوگوی باکیفیت را در `extracted/` بگذارید یا از **تنظیمات ← شرکت ← لوگو** در برنامه انتخاب کنید.
