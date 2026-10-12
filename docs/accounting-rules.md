# Accounting rules — قواعد حسابداری

All postings are double entry and **balanced separately in every currency**. A conversion between currencies is
never a single line; it always goes through the FX conversion account (3200) with one leg per currency. Lines
also carry a base-currency equivalent (`BaseRate`, `BaseAmount`, base = USD by default, *Settings → حسابداری*)
computed from the document's own rate snapshot — never from today's rate.

Sign convention in reports: balance = Σ debit − Σ credit. Positive = **بدهکار (بد)**, negative = **بستانکار (بس)**.
A customer with a USD credit balance is therefore *بستانکار*: the company owes him/her USD.

## 1. Customer receipt (رسید دریافت وجه) — `OperationType.CustomerReceipt`

Receipt of `A` in IRT/IRR (or any currency) converted to the case target currency at `1 Target = R Quote`:

| # | Account | Dr | Cr | Currency |
|---|---|---|---|---|
| 1 | Bank (per deposit; Iran cash 1100 when there are no deposits) | deposit | | receipt currency |
| 2 | Customer | | A | receipt currency |
| 3 | Customer | A | | receipt currency |
| 4 | FX conversion 3200 | | A | receipt currency |
| 5 | FX conversion 3200 | T = A ÷ R | | target |
| 6 | Customer | | T | target |

Lines 3–6 exist only when receipt and target currency differ (IRR↔IRT is a fixed ×10 conversion, no rate needed).
Deposits in IRR on an IRT receipt are converted with the fixed factor.

**Deposit mismatch.** If Σ deposits ≠ receipt amount, the difference is posted to *Discrepancy 4300*
(Dr when deposits are short, Cr when they exceed). It requires explicit acceptance; operators can accept only when
*Settings → حسابداری → اجازه مغایرت* is on (default **off**); admins always can. Accepted mismatch marks the case
**دارای مغایرت**.

**Duplicate deposits.** Before finalizing, every deposit is compared with existing ones: same bank tracking
number (almost certainly a duplicate) or same bank + amount + currency + calendar day (possible duplicate).
Finalization is blocked until the user confirms (`acceptDuplicates`).

The receipt number is allocated only when the receipt is finalized (drafts have no number).

## 2. Exchanger commitment (تعهد صرافی) — `OperationType.ExchangerCommitment`

Toman `P` paid to a Turkish exchanger who commits `C = P ÷ R` USD:

| Account | Dr | Cr | Currency |
|---|---|---|---|
| Exchanger | P | | IRT |
| Bank (funding bank) or Iran cash | | P | IRT |
| Exchanger | | P | IRT |
| FX conversion 3200 | P | | IRT |
| FX conversion 3200 | | C | USD |
| Exchanger | C | | USD |

Result: the exchanger account shows a USD **debit** = what the exchanger still owes us.
*Mark ready for delivery* only sets a flag (status «آماده تحویل دلار»), no posting.

## 3. Delivery to agent (تحویل به نماینده) — `OperationType.AgentDelivery`

Registering a delivery creates a pending document with an `AD…` tracking number; nothing is posted until the
agent confirms. Deliveries beyond the commitment are rejected. On **approval**:

| Account | Dr | Cr | Currency |
|---|---|---|---|
| Agent funds | D | | USD |
| Exchanger | | D | USD |

Rejection posts nothing.

## 4. China receipt (دریافت در چین)

On **registration** (the agent sends the money) — `OperationType.ChinaDispatch`; requires the agent to hold enough USD:

| Account | Dr | Cr | Currency |
|---|---|---|---|
| In transit to China 1500 | S | | USD |
| Agent funds | | S | USD |

On **approval** (the final recipient confirms) — `OperationType.ChinaReceipt`:

| Account | Dr | Cr | Currency |
|---|---|---|---|
| In transit 1500 | | S | USD |
| Recipient | S | | USD (paid in USD) |

When paid in CNY or USDT at `1 USD = R CNY` the recipient line is replaced by
`Dr FX 3200 S USD`, `Cr FX 3200 S×R CNY`, `Dr Recipient S×R CNY`. The recipient must accept the currency
(*China receipts → ارزهای قابل دریافت*).

**Rejection** reverses the dispatch entry (the money is back with the agent).

## 5. Customer payment / refund — `OperationType.CustomerPayment`

`Dr Customer / Cr Bank or Iran cash` in the paid currency (`ReceiptService.RegisterCustomerPaymentAsync`).
No UI screen yet (see open questions).

## 6. Reversal (برگشت سند)

* Only users with *ReverseEntries* (admins by default) can reverse.
* Reversal never edits or deletes: it posts a mirror entry (debits ↔ credits, same date unless given),
  `OperationType.Reversal`, links `ReversalOfEntryId` / `ReversedByEntryId`, and marks the original *Reversed*.
* Dependency order is enforced: a receipt cannot be reversed while the case has a posted exchanger commitment;
  a commitment cannot be reversed while it has approved, non-reversed deliveries. A China receipt reversal reverses
  its approval entry and its dispatch entry.
* Reversed originals and their reversals stay in statements (they net to zero); the statement filter
  «بدون اسناد برگشتی» hides both.
* A case can be cancelled only when it has no live posted entries.

## 7. Transfer status (derived)

`TransferStatusCalculator` recomputes the status after every change, first match wins:

1. Cancelled → **ابطال‌شده**
2. No posted receipt → **پیش‌نویس**
3. Discrepancy flag, accepted deposit mismatch, received more than target, or delivered more than committed → **دارای مغایرت**
4. Received in China ≥ target **and** every commitment fully delivered → **تسویه‌شده**
5. Received in China ≥ target → **دریافت کامل در چین**
6. Received in China > 0 → **دریافت جزئی در چین**
7. Pending China dispatch > 0 → **در مسیر چین**
8. Delivered to agents > 0 → **تحویل به نماینده**
9. Any commitment marked ready → **آماده تحویل دلار**
10. Any posted commitment → **در انتظار صرافی ترکیه**
11. Otherwise → **دریافت وجه در ایران**

## 8. Reports

* **Statement (صورتحساب)**: per currency, opening balance = all posted lines before the period; rows in date order
  with running balance; closing = opening + Σ debit − Σ credit. Filters: currency, operation type, exclude reversed.
  Currencies are never added together.
* **All customers**: per customer and currency — transaction count, total received (credits), total paid (debits),
  balance, last transaction; totals per currency.
* **Party balances**: exchangers, agents, recipients, banks — period debit/credit and closing balance.
* **Open cases**: target, committed, delivered, received in China, unsettled amount and issues.

Date presets (today, yesterday, a specific day, this/last week — the Persian week starts on Saturday —, this/last
Shamsi month, this Shamsi year, custom, all) always cover whole local days in the business time zone.

## 9. Known accounting simplifications (see open-questions.md)

* After settlement the customer keeps a USD credit balance equal to what was delivered; it is not automatically
  cleared against the China receipt.
* FX differences (e.g. receipt rate ≠ exchanger rate) stay as a residual in FX conversion 3200; there is no
  automatic transfer to FX gain/loss 4200.
* Fees (4100) have an account but no posting flow yet.
