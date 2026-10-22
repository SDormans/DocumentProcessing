import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const rootDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const defaultOutput = path.join(rootDirectory, "evaluation-data", "invoices.jsonl");
const suppliers = [
  "Aster Vale Office Supply",
  "Blue Mesa Components",
  "Copper Finch Consulting",
  "Driftwood Labs",
  "Elm Harbor Packaging",
  "Fable Ridge Equipment",
  "Granite Field Services",
  "Juniper Arc Software",
  "Kite & Harbor Logistics",
  "Lumen Grove Stationery"
];
const currencies = ["USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF", "SEK"];
const taxRates = [0, 5, 7, 8.25, 10, 20, 21];
const fields = [
  "Invoice Number",
  "Supplier Name",
  "Invoice Date",
  "Due Date",
  "Currency",
  "Subtotal",
  "Tax",
  "Total"
];

function round(value, digits) {
  const factor = 10 ** digits;
  return Math.round((value + Number.EPSILON) * factor) / factor;
}

function isoDate(date) {
  return date.toISOString().slice(0, 10);
}

function displayDate(date, format) {
  const year = date.getUTCFullYear();
  const month = date.getUTCMonth() + 1;
  const day = date.getUTCDate();
  const monthName = date.toLocaleString("en-US", { month: "short", timeZone: "UTC" });

  switch (format) {
    case 0: return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
    case 1: return `${String(month).padStart(2, "0")}/${String(day).padStart(2, "0")}/${year}`;
    case 2: return `${String(day).padStart(2, "0")}.${String(month).padStart(2, "0")}.${year}`;
    case 3: return `${String(day).padStart(2, "0")} ${monthName} ${year}`;
    default: return `${monthName} ${String(day).padStart(2, "0")}, ${year}`;
  }
}

function formatAmount(amount, digits) {
  return amount.toFixed(digits);
}

function createText(record, index, invoiceDate, dueDate, subtotal, tax, total, digits) {
  const supplier = record.expected.supplierName;
  const invoiceNumber = record.expected.invoiceNumber;
  const dateText = displayDate(invoiceDate, index % 5);
  const dueText = dueDate ? displayDate(dueDate, (index + 2) % 5) : null;
  const lines = [];

  if (index % 3 === 0) {
    lines.push("INVOICE", supplier, `Invoice No: ${invoiceNumber}`, `Invoice Date: ${dateText}`);
    if (dueText) lines.push(`Due Date: ${dueText}`);
    lines.push(`Currency: ${record.expected.currency}`, "Description     Qty     Unit Price");
  } else if (index % 3 === 1) {
    lines.push(`${supplier} - BILL`, `Invoice #: ${invoiceNumber}`, `Issued: ${dateText}`);
    if (dueText) lines.push(`Payment due: ${dueText}`);
    lines.push(`Amounts in ${record.expected.currency}`, "Item / Quantity / Amount");
  } else {
    lines.push("COMMERCIAL INVOICE", `Supplier: ${supplier}`, `Reference ${invoiceNumber}`);
    lines.push(`Date of issue ${dateText}`);
    if (dueText) lines.push(`Pay before ${dueText}`);
    lines.push(`Currency code ${record.expected.currency}`, "Service description");
  }

  lines.push(`Synthetic services ${String(index + 1).padStart(2, "0")}  1  ${formatAmount(subtotal, digits)}`);
  if (index % 10 === 8) {
    const conflictingDate = new Date(dueDate.getTime() + 7 * 24 * 60 * 60 * 1000);
    lines.push(`Alternative due date: ${displayDate(conflictingDate, (index + 1) % 5)}`);
  }
  lines.push(
    `Subtotal: ${formatAmount(subtotal, digits)} ${record.expected.currency}`,
    `Tax: ${formatAmount(tax, digits)} ${record.expected.currency}`,
    `Total: ${formatAmount(total, digits)} ${record.expected.currency}`
  );
  return lines.join("\n");
}

export function generateDataset() {
  return Array.from({ length: 50 }, (_, index) => {
    const currency = currencies[index % currencies.length];
    const digits = currency === "JPY" ? 0 : 2;
    const invoiceDate = new Date(Date.UTC(2025 + (index % 2), (index * 7) % 12, ((index * 11) % 27) + 1));
    const dueDate = index % 10 === 0
      ? null
      : new Date(invoiceDate.getTime() + (14 + (index % 17)) * 24 * 60 * 60 * 1000);
    const subtotal = round(35.27 + index * 19.43, digits);
    const taxRate = taxRates[index % taxRates.length];
    const taxAmount = round(subtotal * taxRate / 100, digits);
    const isInvalid = index % 10 === 7;
    const totalAmount = round(subtotal + taxAmount + (isInvalid ? 11 : 0), digits);
    const supplierName = `${suppliers[index % suppliers.length]} ${String(Math.floor(index / suppliers.length) + 1).padStart(2, "0")}`;
    const invoiceNumber = `SYN-${2025 + (index % 2)}-${String(index + 1).padStart(4, "0")}`;
    const requiresHumanReview = isInvalid || index % 10 === 8 || index % 4 === 0;

    const record = {
      id: `SYNTH-INV-${String(index + 1).padStart(4, "0")}`,
      datasetVersion: "1.0.0",
      expectedValid: !isInvalid,
      expectedHumanReview: requiresHumanReview,
      quality: isInvalid ? "inconsistent-totals" : index % 10 === 8 ? "conflicting-dates" : "synthetic-text",
      expected: {
        invoiceNumber,
        supplierName,
        invoiceDate: isoDate(invoiceDate),
        dueDate: dueDate ? isoDate(dueDate) : null,
        currency,
        subtotal,
        taxAmount,
        totalAmount,
        lineItems: [{
          description: `Synthetic services ${String(index + 1).padStart(2, "0")}`,
          quantity: 1,
          unitPrice: subtotal,
          amount: subtotal
        }]
      },
      documentText: ""
    };

    record.documentText = createText(record, index, invoiceDate, dueDate, subtotal, taxAmount, totalAmount, digits);
    return record;
  });
}

function escapePdfText(value) {
  return value.replaceAll("\\", "\\\\").replaceAll("(", "\\(").replaceAll(")", "\\)");
}

function createPdf(text) {
  const textOperations = text.split("\n")
    .map((line, index) => `${index === 0 ? "" : "0 -15 Td\n"}(${escapePdfText(line)}) Tj`)
    .join("\n");
  const stream = Buffer.from(`BT\n/F1 10 Tf\n48 780 Td\n${textOperations}\nET\n`, "ascii");
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 3 0 R >> >> /Contents 5 0 R >>",
    `<< /Length ${stream.length} >>\nstream\n${stream.toString("ascii")}endstream`
  ];
  let pdf = "%PDF-1.4\n";
  const offsets = [0];
  for (let index = 0; index < objects.length; index++) {
    offsets.push(Buffer.byteLength(pdf, "ascii"));
    pdf += `${index + 1} 0 obj\n${objects[index]}\nendobj\n`;
  }
  const xrefOffset = Buffer.byteLength(pdf, "ascii");
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets.slice(1)) {
    pdf += `${String(offset).padStart(10, "0")} 00000 n \n`;
  }
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF\n`;
  return Buffer.from(pdf, "ascii");
}

export async function writeDataset(records, outputPath = defaultOutput, pdfDirectory = null) {
  await mkdir(path.dirname(outputPath), { recursive: true });
  await writeFile(outputPath, `${records.map(record => JSON.stringify(record)).join("\n")}\n`, "utf8");

  if (pdfDirectory) {
    await mkdir(pdfDirectory, { recursive: true });
    for (const record of records) {
      await writeFile(
        path.join(pdfDirectory, `${record.id}.pdf`),
        createPdf(record.documentText)
      );
    }
  }
}

const args = process.argv.slice(2);
if (args[0] === "--run") {
  const outputIndex = args.indexOf("--output");
  const pdfIndex = args.indexOf("--pdf-dir");
  const outputPath = outputIndex >= 0 ? path.resolve(args[outputIndex + 1]) : defaultOutput;
  const pdfDirectory = pdfIndex >= 0 ? path.resolve(args[pdfIndex + 1]) : null;
  if ((outputIndex >= 0 && !args[outputIndex + 1]) || (pdfIndex >= 0 && !args[pdfIndex + 1])) {
    throw new Error("Both --output and --pdf-dir require a path.");
  }
  const records = generateDataset();
  await writeDataset(records, outputPath, pdfDirectory);
  console.log(`Generated ${records.length} synthetic invoice records at ${outputPath}.`);
  if (pdfDirectory) console.log(`Generated text-based PDF fixtures at ${pdfDirectory}.`);
}
