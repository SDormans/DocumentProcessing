import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const rootDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const defaultDatasetPath = path.join(rootDirectory, "evaluation-data", "invoices.jsonl");
const fieldNames = [
  "invoiceNumber",
  "supplierName",
  "invoiceDate",
  "dueDate",
  "currency",
  "subtotal",
  "taxAmount",
  "totalAmount",
  "lineItems"
];
const numericFields = new Set(["subtotal", "taxAmount", "totalAmount"]);

function normalizeText(value) {
  return value.trim().replace(/\s+/g, " ").toLocaleLowerCase("en-US");
}

function normalizeJson(value) {
  if (Array.isArray(value)) return value.map(normalizeJson);
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.keys(value).sort().map(key => [key, normalizeJson(value[key])])
    );
  }
  if (typeof value === "string") return normalizeText(value);
  return value;
}

function sameValue(field, expected, actual) {
  if (numericFields.has(field) && typeof expected === "number" && typeof actual === "number") {
    return Math.abs(expected - actual) <= 0.01;
  }
  if (field === "lineItems") {
    return JSON.stringify(normalizeJson(expected)) === JSON.stringify(normalizeJson(actual));
  }
  if (typeof expected === "string" && typeof actual === "string") {
    return normalizeText(expected) === normalizeText(actual);
  }
  return expected === actual;
}

function isFieldValueValid(field, value) {
  if (value === null) return true;
  if (numericFields.has(field)) return typeof value === "number" && Number.isFinite(value);
  if (field === "lineItems") return Array.isArray(value);
  return typeof value === "string";
}

export function validatePredictionSchema(prediction) {
  if (!prediction || typeof prediction !== "object" || Array.isArray(prediction)) return false;
  if (typeof prediction.id !== "string" ||
      typeof prediction.success !== "boolean" ||
      typeof prediction.valid !== "boolean" ||
      typeof prediction.requiresHumanReview !== "boolean" ||
      !prediction.fields ||
      typeof prediction.fields !== "object" ||
      Array.isArray(prediction.fields)) {
    return false;
  }
  if (!fieldNames.every(field =>
    Object.hasOwn(prediction.fields, field) && isFieldValueValid(field, prediction.fields[field]))) {
    return false;
  }
  for (const key of ["durationMs", "ocrDurationMs", "inferenceDurationMs", "queueWaitMs", "aiCostUsd", "retryCount"]) {
    if (prediction[key] !== undefined &&
        (typeof prediction[key] !== "number" || !Number.isFinite(prediction[key]) || prediction[key] < 0)) {
      return false;
    }
  }
  return true;
}

function summarizeNumbers(values) {
  if (values.length === 0) return { count: 0, average: null, median: null };
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  const median = sorted.length % 2 === 0
    ? (sorted[middle - 1] + sorted[middle]) / 2
    : sorted[middle];
  return {
    count: sorted.length,
    average: values.reduce((sum, value) => sum + value, 0) / values.length,
    median
  };
}

export function evaluateDataset(dataset, predictions) {
  const datasetById = new Map();
  for (const record of dataset) {
    if (!record || typeof record.id !== "string" || datasetById.has(record.id)) {
      throw new Error("Dataset records must have unique string IDs.");
    }
    datasetById.set(record.id, record);
  }

  const predictionsById = new Map();
  let invalidPredictionRecords = 0;
  let extraPredictionCount = 0;
  for (const prediction of predictions) {
    if (!prediction || typeof prediction.id !== "string") {
      invalidPredictionRecords++;
      continue;
    }
    if (!datasetById.has(prediction.id)) {
      extraPredictionCount++;
      continue;
    }
    if (predictionsById.has(prediction.id)) {
      throw new Error(`Duplicate prediction for dataset record ${prediction.id}.`);
    }
    predictionsById.set(prediction.id, prediction);
  }

  const fieldCounts = Object.fromEntries(fieldNames.map(field => [
    field,
    { exactMatchCount: 0, evaluatedCount: 0, missingCount: 0, incorrectCount: 0 }
  ]));
  let schemaFailureCount = 0;
  let extractionFailureCount = 0;
  let acceptedInvalidCount = 0;
  let rejectedValidCount = 0;
  let humanReviewCount = 0;
  const durationSamples = [];
  const ocrDurationSamples = [];
  const inferenceDurationSamples = [];
  const queueWaitSamples = [];
  const costSamples = [];
  let totalRetries = 0;
  let retrySamples = 0;
  const recordResults = [];

  for (const record of dataset) {
    const prediction = predictionsById.get(record.id);
    if (!prediction) {
      recordResults.push({ id: record.id, status: "missing-prediction" });
      continue;
    }

    const schemaValid = validatePredictionSchema(prediction);
    if (!schemaValid) schemaFailureCount++;
    if (prediction.success !== true) extractionFailureCount++;
    if (prediction.valid === true && record.expectedValid === false) acceptedInvalidCount++;
    if (prediction.valid === false && record.expectedValid === true) rejectedValidCount++;
    if (prediction.requiresHumanReview === true) humanReviewCount++;
    if (typeof prediction.durationMs === "number") durationSamples.push(prediction.durationMs);
    if (typeof prediction.ocrDurationMs === "number") ocrDurationSamples.push(prediction.ocrDurationMs);
    if (typeof prediction.inferenceDurationMs === "number") inferenceDurationSamples.push(prediction.inferenceDurationMs);
    if (typeof prediction.queueWaitMs === "number") queueWaitSamples.push(prediction.queueWaitMs);
    if (typeof prediction.aiCostUsd === "number") costSamples.push(prediction.aiCostUsd);
    if (typeof prediction.retryCount === "number") {
      totalRetries += prediction.retryCount;
      retrySamples++;
    }

    const differences = [];
    const fields = prediction.fields ?? {};
    for (const field of fieldNames) {
      const expected = record.expected[field];
      const actual = fields[field] ?? null;
      const counts = fieldCounts[field];
      counts.evaluatedCount++;
      if (expected !== null && actual === null) {
        counts.missingCount++;
        differences.push({ field, result: "missing" });
      } else if (sameValue(field, expected, actual)) {
        counts.exactMatchCount++;
      } else {
        counts.incorrectCount++;
        differences.push({ field, result: "incorrect" });
      }
    }
    recordResults.push({
      id: record.id,
      status: prediction.success === true ? "extracted" : "failed",
      schemaValid,
      expectedValid: record.expectedValid,
      predictedValid: prediction.valid,
      expectedHumanReview: record.expectedHumanReview,
      predictedHumanReview: prediction.requiresHumanReview,
      differences
    });
  }

  for (const counts of Object.values(fieldCounts)) {
    counts.accuracy = counts.evaluatedCount === 0
      ? null
      : counts.exactMatchCount / counts.evaluatedCount;
  }
  const allFieldCounts = Object.values(fieldCounts).reduce((totals, field) => ({
    exact: totals.exact + field.exactMatchCount,
    evaluated: totals.evaluated + field.evaluatedCount,
    missing: totals.missing + field.missingCount,
    incorrect: totals.incorrect + field.incorrectCount
  }), { exact: 0, evaluated: 0, missing: 0, incorrect: 0 });
  const numericCounts = [...numericFields].map(field => fieldCounts[field])
    .reduce((totals, field) => ({
      exact: totals.exact + field.exactMatchCount,
      evaluated: totals.evaluated + field.evaluatedCount
    }), { exact: 0, evaluated: 0 });
  const predictionCount = predictionsById.size;

  return {
    reportVersion: "1.0.0",
    generatedAtUtc: new Date().toISOString(),
    dataset: {
      version: dataset[0]?.datasetVersion ?? null,
      count: dataset.length,
      syntheticValidInvoices: dataset.filter(record => record.expectedValid === true).length,
      syntheticInvalidInvoices: dataset.filter(record => record.expectedValid === false).length
    },
    predictions: {
      matched: predictionCount,
      missing: dataset.length - predictionCount,
      extra: extraPredictionCount,
      invalidRecords: invalidPredictionRecords,
      coverage: dataset.length === 0 ? null : predictionCount / dataset.length,
      successfulExtractions: predictionCount - extractionFailureCount,
      failedExtractions: extractionFailureCount
    },
    quality: {
      fieldAccuracy: allFieldCounts.evaluated === 0 ? null : allFieldCounts.exact / allFieldCounts.evaluated,
      numericFieldAccuracy: numericCounts.evaluated === 0 ? null : numericCounts.exact / numericCounts.evaluated,
      fields: fieldCounts,
      missingFieldCount: allFieldCounts.missing,
      incorrectFieldCount: allFieldCounts.incorrect,
      invalidExtractionRate: predictionCount === 0 ? null : extractionFailureCount / predictionCount,
      schemaValidationFailureRate: predictionCount === 0 ? null : schemaFailureCount / predictionCount,
      schemaValidationFailures: schemaFailureCount,
      incorrectlyAcceptedInvalidInvoices: acceptedInvalidCount,
      incorrectlyRejectedValidInvoices: rejectedValidCount,
      humanReviewRate: predictionCount === 0 ? null : humanReviewCount / predictionCount,
      humanReviewCount
    },
    operations: {
      processingDurationMs: summarizeNumbers(durationSamples),
      ocrDurationMs: summarizeNumbers(ocrDurationSamples),
      inferenceDurationMs: summarizeNumbers(inferenceDurationSamples),
      queueWaitMs: summarizeNumbers(queueWaitSamples),
      processingFailureRate: predictionCount === 0 ? null : extractionFailureCount / predictionCount,
      retryCount: retrySamples === 0 ? null : totalRetries,
      estimatedAiCostUsd: summarizeNumbers(costSamples)
    },
    recordResults,
    limitations: predictionCount === 0
      ? ["No prediction file was provided; model quality and operational metrics are unavailable."]
      : []
  };
}

async function readJsonLines(filePath) {
  const content = await readFile(filePath, "utf8");
  return content.split(/\r?\n/).filter(line => line.trim()).map((line, index) => {
    try {
      return JSON.parse(line);
    } catch (error) {
      throw new Error(`Invalid JSON on line ${index + 1} of ${filePath}: ${error.message}`);
    }
  });
}

async function run() {
  const args = process.argv.slice(2);
  const options = new Map();
  for (let index = 0; index < args.length; index++) {
    const key = args[index];
    if (!["--dataset", "--predictions", "--report"].includes(key) || options.has(key)) {
      throw new Error(`Unexpected or duplicate command-line argument: ${key}`);
    }
    const value = args[index + 1];
    if (!value || value.startsWith("--")) {
      throw new Error(`${key} requires a path.`);
    }
    options.set(key, value);
    index++;
  }

  const datasetPath = options.has("--dataset")
    ? path.resolve(options.get("--dataset"))
    : defaultDatasetPath;
  const predictionsPath = options.has("--predictions")
    ? path.resolve(options.get("--predictions"))
    : null;
  const reportPath = options.has("--report")
    ? path.resolve(options.get("--report"))
    : null;
  const dataset = await readJsonLines(datasetPath);
  const predictions = predictionsPath ? await readJsonLines(predictionsPath) : [];
  const report = evaluateDataset(dataset, predictions);
  const output = `${JSON.stringify(report, null, 2)}\n`;
  if (reportPath) {
    await writeFile(reportPath, output, "utf8");
    console.log(`Evaluation report saved to ${reportPath}.`);
  }
  console.log(output);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await run();
}
