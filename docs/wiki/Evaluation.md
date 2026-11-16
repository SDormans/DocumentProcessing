# Evaluation

The goal is to measure extraction quality, rather than demo it.

## Dataset

[`evaluation-data/invoices.jsonl`](https://github.com/SDormans/DocumentProcessing/blob/master/evaluation-data/invoices.jsonl): 50 synthetic invoices, 45 valid and 5 deliberately invalid. They cover several suppliers, eight currencies, five date formats, missing due dates, conflicting dates and inconsistent totals. There is no real customer data. All of them are text PDFs; there are no scanned images yet.

```bash
npm run dataset:generate   # regenerate the manifest (deterministic)
npm run dataset:pdfs       # also write PDFs to evaluation-data/generated/pdfs (git-ignored)
```

## Predictions format

One JSON object per line:

```json
{"id":"SYNTH-INV-0001","success":true,"valid":true,"requiresHumanReview":false,
 "fields":{"invoiceNumber":"…","supplierName":"…","invoiceDate":"…","dueDate":null,"currency":"EUR",
           "subtotal":100.0,"taxAmount":21.0,"totalAmount":121.0,"lineItems":[]},
 "durationMs":2300,"inferenceDurationMs":2100,"retryCount":0,"aiCostUsd":0.0012}
```

## Scoring

```bash
npm run evaluate -- --predictions predictions.jsonl --report report.json
```

The report contains per-field and overall exact-match accuracy (text comparison ignores case and whitespace), numeric accuracy within 0.01, missing and incorrect values, schema-failure and invalid-extraction rates, false acceptance and rejection of invalid or valid invoices, the human-review rate, and duration, retry and cost summaries.

## Current results

No model has been scored yet. The baseline without predictions reports 0% coverage, and that is intentional: the tool never invents numbers. Producing the first real scorecard with the OpenAI provider is milestone 2 on the [Roadmap](Roadmap.md).
