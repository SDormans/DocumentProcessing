import assert from "node:assert/strict";
import test from "node:test";
import { generateDataset } from "./generate-dataset.mjs";
import { evaluateDataset } from "./evaluate.mjs";

const dataset = generateDataset();

function makePrediction(record, overrides = {}) {
  return {
    id: record.id,
    success: true,
    valid: record.expectedValid,
    requiresHumanReview: record.expectedHumanReview,
    fields: { ...record.expected },
    durationMs: 100,
    ...overrides
  };
}

test("synthetic dataset contains 50 varied ground-truth invoices", () => {
  assert.equal(dataset.length, 50);
  assert.equal(new Set(dataset.map(record => record.id)).size, 50);
  assert.equal(dataset.filter(record => record.expectedValid).length, 45);
  assert.equal(dataset.filter(record => !record.expectedValid).length, 5);
  assert.equal(new Set(dataset.map(record => record.expected.currency)).size, 8);
  assert.ok(dataset.some(record => record.expected.dueDate === null));
  assert.ok(dataset.some(record => record.quality === "conflicting-dates"));
  assert.ok(dataset.some(record => record.quality === "inconsistent-totals"));
  assert.ok(dataset.every(record => record.documentText.includes(record.expected.invoiceNumber)));
});

test("exact predictions produce complete field-level scores", () => {
  const predictions = dataset.map(record => makePrediction(record));
  const report = evaluateDataset(dataset, predictions);

  assert.equal(report.predictions.matched, 50);
  assert.equal(report.quality.fieldAccuracy, 1);
  assert.equal(report.quality.numericFieldAccuracy, 1);
  assert.equal(report.quality.incorrectlyAcceptedInvalidInvoices, 0);
  assert.equal(report.quality.incorrectlyRejectedValidInvoices, 0);
  assert.equal(report.quality.schemaValidationFailureRate, 0);
  assert.equal(report.operations.processingDurationMs.average, 100);
});

test("evaluation distinguishes missing values, incorrect values, and invalid acceptance", () => {
  const record = dataset[0];
  const prediction = makePrediction(record, {
    valid: true,
    fields: {
      ...record.expected,
      supplierName: null,
      subtotal: record.expected.subtotal + 1,
      totalAmount: record.expected.totalAmount + 0.01
    }
  });
  const report = evaluateDataset([record], [prediction]);

  assert.equal(report.quality.fields.supplierName.missingCount, 1);
  assert.equal(report.quality.fields.subtotal.incorrectCount, 1);
  assert.equal(report.quality.fields.totalAmount.exactMatchCount, 1);
  assert.equal(report.quality.missingFieldCount, 1);
  assert.equal(report.quality.incorrectFieldCount, 1);
});

test("evaluation reports a schema failure when prediction fields have invalid types", () => {
  const record = dataset[0];
  const prediction = makePrediction(record, {
    fields: { ...record.expected, subtotal: "not-a-number" }
  });
  const report = evaluateDataset([record], [prediction]);

  assert.equal(report.quality.schemaValidationFailures, 1);
  assert.equal(report.quality.schemaValidationFailureRate, 1);
});

test("no predictions produce unavailable rather than fabricated quality metrics", () => {
  const report = evaluateDataset(dataset, []);

  assert.equal(report.predictions.missing, 50);
  assert.equal(report.predictions.coverage, 0);
  assert.equal(report.quality.fieldAccuracy, null);
  assert.equal(report.quality.invalidExtractionRate, null);
  assert.equal(report.operations.processingDurationMs.average, null);
  assert.ok(report.limitations.some(message => message.includes("unavailable")));
});

test("duplicate predictions are rejected", () => {
  const prediction = makePrediction(dataset[0]);

  assert.throws(
    () => evaluateDataset([dataset[0]], [prediction, prediction]),
    /Duplicate prediction/
  );
});
