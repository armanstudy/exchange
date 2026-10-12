# Open questions — پرسش‌های باز

Where the spec says "ask the user", the app ships with a **configurable, documented default**. Each item below
lists the question, the current default and where to change it. Please answer these before production use.

## A. Receipt numbering

1. **Meaning of `YI140507126LE`.** Default: `YI` + Shamsi year `1405` + month `07` + 3-digit sequence `126` + `LE`,
   sequence restarting monthly — pattern `YI{yyyy}{MM}{seq:3}LE`, scope Monthly.
   Alternatives: `07` = day, or `126` = day-of-year, or a global/yearly counter.
   The reference file name `140507012` doesn't fit this reading.
   Change in **Settings → Numbering** (pattern tokens `{yyyy} {yy} {MM} {dd} {seq:N}`; scope Global/Yearly/Monthly/Daily).
2. **Starting sequence.** Is there an existing paper series to continue from? Default: start at 1.
3. **Digits and separators.** Default: Latin digits everywhere in PDFs (receipt numbers, amounts, dates), with
   `,` as the thousands separator. Input accepts Persian/Arabic digits and separators. Should PDFs use Persian
   digits (`۱۲۳`) and the `٬` separator?

## B. Receipt template and branding

4. **Template fields omitted.** The template also has phone, national ID, address, a deposits table,
   currency/payment-method checkboxes and the amount in words. The spec listed only five customer fields, so the PDF
   keeps those five (payer, amount + currency, rate, final recipient) plus receipt number and date. It uses the
   template's layout, colours and letterhead. Should the extra fields be added?
5. **Duplicate phone.** The template shows the Iran phone `+98917 177 2028` twice. Only one copy is used. Is there a
   second number?
6. **Logo quality.** Both available logos are small: the one in the docx is 156×138 px, and the user-supplied JPEG
   had no transparency, so its alpha channel was rebuilt automatically.
   A vector (SVG/PDF) or high-res PNG is needed. You can replace it in **Settings → Company → Logo**.
7. **Font.** The template uses B Nazanin, which cannot be redistributed freely. The app embeds **Vazirmatn** (OFL).
   Is that acceptable?
8. **Template typos.** Some Persian texts in the template look mistyped. Texts were normalised; please review the
   header/footer wording in Settings.
9. **Customer statement layout.** The reference statement groups rows by date and has a Gregorian date column.
   The current statement is a flat, Shamsi-dated list. Should we match the reference exactly?

## C. Accounting

10. **Base/reporting currency.** Default: USD (`AccountingSettings.BaseCurrency`). Is IRT preferred?
11. **Fees.** There is an account (4100) but no fee flow. Who pays fees (customer, exchanger, agent)? Fixed or
    percentage? In which currency?
12. **Customer USD liability after China receipt.** Today the customer's USD balance from the receipt is *not*
    automatically discharged when China confirms receipt (see accounting-rules.md §9). Should approval post a
    settlement entry?
13. **FX gain/loss.** Rate differences (receipt rate vs exchanger rate) remain in FX conversion 3200. Should they be
    moved to FX gain/loss 4200 automatically? When — per case, or at period end?
14. **Deposit mismatch policy.** Default: sum of deposits must equal the receipt amount. Only users with
    "reverse entries" permission may override, unless **Settings → Accounting → Allow deposit mismatch** is on.
    Is a tolerance needed?
15. **Customer payment/refund.** `CustomerPaymentService` exists and is tested, but has no screen. Is it needed in v1?

## D. Operations

16. **Business timezone.** Default: the machine's local time zone (Iran). Dates are stored in UTC and converted
    to Shamsi. Should China-side documents use China time?
17. **Operator permission set.** Default: operators cannot reverse entries, approve deliveries/China receipts, edit
    rates, manage users/settings, or restore backups. Admins can grant extras per user. Is that right?
18. **Backup schedule.** Default: automatic backup every 24 h while the app is running, plus one on exit, into the
    backup folder (Settings → Backup). There is no off-site copy. Is a copy to a USB drive or network share needed?
    The last 10 backups are kept by default. Is that enough?
19. **Multi-user / network.** The database is a local SQLite file for a single PC. Will several PCs need to work
    on the same data?

## E. Legal / licensing

20. **QuestPDF Community licence.** It is free for companies with < USD 1M annual revenue. Please confirm
    eligibility; otherwise a commercial licence is needed.
21. **Code signing.** The installer is unsigned, so SmartScreen warns. Is a code-signing certificate available?

## F. Not verified in this build environment (Linux)

- WPF runtime behaviour, bindings and layout (compiled only; not run).
- Inno Setup compilation of `installer/LenjExchange.iss`.
- Printing via the Windows shell "print" verb, and the folder/file dialogs.
