# Sample EDI files

Synthetic 835/837 EDI files for end-to-end testing. Not real patient data —
made up names, made up NPIs, made up insurance IDs.

| File | Format | What's inside |
|---|---|---|
| `paired_835i.edi` | 835I (institutional remit) | One claim `CLM_PAIR_001`: HCPCS 99213, $150 billed, **$0 paid** (denied as packaged with reason CO/PR-1), DOS 2026-04-23 |
| `paired_837i.edi` | 837I (institutional submission) | Same claim `CLM_PAIR_001` from the **submission** side: HCPCS 99213, billed $150, **principal diagnosis E11.9** (Type 2 diabetes) |

## What this pair demonstrates

The two files share `CLM_PAIR_001` as their CLP01/CLM01 claim ID. When both are uploaded, the auto-linker will:

1. **Find the pair** — both rows in `parsed_claim` share the same `claim_id`
2. **Bidirectionally link them** — `linked_claim_id_fk` on each side
3. **Enrich the 835 with the 837's diagnosis** — the 835 alone has no `principal_diagnosis` (835s never carry dx codes); the linker copies `E11.9` from the 837 → 835
4. **Re-run the APG engine on the enriched 835** — now the engine sees `99213 + E119`, fires the **visit-purpose adjustment** (because EAPG 491 is an Incidental placeholder), and re-prices the claim to **$132.09** (Clinic MR/DD/TBI Downstate, base rate $204.85, EAPG 713 weight 0.6448)

### Before linking

The 835I row alone shows:

| Field | Value |
|---|---|
| Correct APG payment | **$0.00** (99213 → EAPG 491 packaged, no dx to override with) |
| Variance | $0.00 |
| Status | Match |

### After linking

The same 835I row now shows:

| Field | Value |
|---|---|
| Correct APG payment | **$132.09** ⬅️ visit-purpose override fired |
| Variance | **$132.09** |
| Status | Underpaid |
| 🔗 badge | yes |
| Per-line note on 99213 | *"Visit-purpose adjustment: HCPCS 99213 maps to Incidental placeholder EAPG 491; using ICD-10 E119's EAPG 713 instead."* |

This is the same scenario the Rate Calculator demonstrates manually — but driven by real EDI files going through the upload pipeline.

## How to run the demo

In the app:

1. Make sure your active provider is **Test Clinic / MANHATTAN / Clinic MR/DD/TBI / Downstate** (Provider Config page).
2. Upload `paired_835i.edi` first (file type **835I**).
3. In Claims, find `CLM_PAIR_001` — Correct APG = **$0.00**, status Match. **No 🔗 badge yet.**
4. Upload `paired_837i.edi` (file type **837I**).
5. The upload result banner now includes: *"Linked 1 837↔835 pair(s); re-priced 1 claim(s) with enriched dx codes."*
6. Refresh **Claims**. The 835I row for `CLM_PAIR_001` has changed:
   - 🔗 badge appeared next to the file-type label
   - Correct APG flipped from $0.00 → **$132.09**
   - Status flipped from Match → Underpaid
7. Click **View** on either row → header shows *"linked with [other type] claim"* with click-through.

If you upload them in reverse order (837 first, 835 second), you get the same end state — the linker fires after every upload, not just on the second one.
