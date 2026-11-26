using System.Globalization;
using System.IO.Compression;
using System.Text;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Reporting;

/// <summary>
/// Minimal, dependency-free PDF 1.4 writer. It renders a paginated text report with Helvetica and
/// Courier base-14 fonts, which every reader supports without embedding font files. Deliberately
/// plain: correctness and offline operation matter more than typography.
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    private const int WrapWidth = 104;

    public ReportFormat Format => ReportFormat.Pdf;
    public string FileExtension => ".pdf";

    public string Render(ReportContext context) =>
        throw new NotSupportedException("PDF is a binary format; use RenderBytes.");

    public byte[] RenderBytes(ReportContext context)
    {
        var pages = new List<string>();
        var page = new StringBuilder();
        var y = 792.0;

        void Break()
        {
            pages.Add(page.ToString());
            page.Clear();
            y = 792.0;
        }

        void Put(string text, string font = "F1", double size = 9.5)
        {
            if (y < 55) Break();
            page.Append(FormattableString.Invariant(
                $"BT /{font} {size:0.#} Tf 50 {y:0.#} Td ({Escape(text)}) Tj ET\n"));
            y -= size + 3;
        }

        void Heading(string text)
        {
            y -= 7;
            Put(text, "F2", 13);
        }

        void Wrapped(string text, string font = "F1", double size = 9)
        {
            foreach (var line in WrapText(text, WrapWidth))
            {
                Put(line, font, size);
            }
        }

        var assessment = context.Assessment;
        var scores = assessment.Scores;
        var type = context.ReportType;

        Put("Purview Governance Copilot", "F2", 19);
        Put($"{type} governance report — {context.OrganizationName}", "F2", 13);
        Put($"Generated {context.GeneratedUtc:yyyy-MM-dd HH:mm} UTC by {context.GeneratedBy}", "F1", 9);
        Put($"Tenant {context.Tenant.TenantName} · Assessment {assessment.Id} · {assessment.RuleCount} rules evaluated", "F1", 9);

        Heading("Scores");
        Put($"Overall {scores.Overall:0.#} · Maturity {scores.MaturityLevel} · Risk exposure {scores.Risk:0.#}", "F1", 10);
        Put($"Security {scores.Security:0.#} · Governance {scores.Governance:0.#} · Compliance {scores.Compliance:0.#}", "F1", 9);
        Put($"AI governance {scores.AiGovernance:0.#} · Copilot readiness {scores.CopilotReadiness:0.#}", "F1", 9);
        Put($"Findings {assessment.Findings.Count} · Recommendations {assessment.Recommendations.Count}", "F1", 9);

        Heading("Module scores");
        Put("Module                                  Score  Assessed   Pass   Warn   Fail", "F3", 8.5);
        foreach (var module in scores.Modules.OrderByDescending(m => m.Assessed))
        {
            Put($"{Truncate(module.Module.ToString(), 38),-38} {module.Score,6:0.#} {module.Assessed,9} {module.Passed,6} {module.Warnings,6} {module.Failed,6}", "F3", 8.5);
        }

        Heading("Framework coverage");
        if (context.Frameworks.Count == 0)
        {
            Put("No framework-mapped findings in this assessment.", "F1", 9);
        }
        else
        {
            foreach (var framework in context.Frameworks)
            {
                Put($"{framework.Framework}: {framework.Percent:0.#}% ({framework.CoveredControls}/{framework.TotalControls})", "F2", 9.5);
                if (framework.Gaps.Count > 0)
                {
                    Wrapped($"Gaps: {string.Join("; ", framework.Gaps.Take(8))}{(framework.Gaps.Count > 8 ? "; …" : string.Empty)}", "F1", 8.5);
                }
            }
        }

        if (ReportProfile.IncludesFindings(type))
        {
            Heading("Findings");
            foreach (var finding in ReportProfile.Findings(assessment, type).Take(80))
            {
                Put($"[{finding.Status}] {finding.Id} — {Truncate(finding.Title, 90)}", "F2", 8.5);
                Wrapped($"Evidence: {finding.Evidence}", "F1", 8);
                if (!string.IsNullOrWhiteSpace(finding.Remediation))
                {
                    Wrapped($"Remediation: {finding.Remediation}", "F1", 8);
                }
                y -= 2;
            }
        }

        if (ReportProfile.IncludesRecommendations(type))
        {
            Heading("Recommendations");
            foreach (var recommendation in ReportProfile.Recommendations(assessment, type).Take(40))
            {
                Put($"{recommendation.Priority} · {recommendation.Id} — {Truncate(recommendation.Title, 85)}", "F1", 8.5);
                Wrapped($"Licences: {string.Join(", ", recommendation.RequiredLicenses)} · Change risk: {recommendation.ChangeRisk}", "F1", 8);
            }
        }

        if (page.Length > 0)
        {
            pages.Add(page.ToString());
        }

        if (pages.Count == 0)
        {
            pages.Add(string.Empty);
        }

        for (var i = 0; i < pages.Count; i++)
        {
            pages[i] += FormattableString.Invariant($"BT /F1 8 Tf 50 30 Td (Page {i + 1} of {pages.Count} — Belle-RajaCorp) Tj ET\n");
        }

        return BuildPdf(pages);
    }

    private static byte[] BuildPdf(IReadOnlyList<string> pages)
    {
        var builder = new StringBuilder();
        var offsets = new List<int>();

        builder.Append("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");

        void Object(int id, string body)
        {
            offsets.Add(builder.Length);
            builder.Append(id.ToString(CultureInfo.InvariantCulture))
                .Append(" 0 obj\n")
                .Append(body)
                .Append("\nendobj\n");
        }

        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(' ', pages.Select((_, i) => FormattableString.Invariant($"{3 + i * 2} 0 R")));
        Object(2, FormattableString.Invariant($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));

        var fontBase = 3 + pages.Count * 2;
        var fonts = FormattableString.Invariant(
            $"/Font << /F1 {fontBase} 0 R /F2 {fontBase + 1} 0 R /F3 {fontBase + 2} 0 R >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var pageId = 3 + i * 2;
            var contentId = pageId + 1;
            var content = pages[i];
            var length = Encoding.Latin1.GetByteCount(content);

            Object(pageId, FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << {fonts} >> /Contents {contentId} 0 R >>"));

            Object(contentId, FormattableString.Invariant($"<< /Length {length} >>\nstream\n{content}\nendstream"));
        }

        Object(fontBase, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Object(fontBase + 1, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Object(fontBase + 2, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

        var objectCount = fontBase + 2;
        var xref = builder.Length;

        builder.Append("xref\n0 ").Append((objectCount + 1).ToString(CultureInfo.InvariantCulture)).Append("\n");
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n")
            .Append(FormattableString.Invariant($"<< /Size {objectCount + 1} /Root 1 0 R >>\n"))
            .Append("startxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static IEnumerable<string> WrapText(string? text, int width)
    {
        foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            var line = new StringBuilder();

            foreach (var word in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var remaining = word;

                while (remaining.Length > width)
                {
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    yield return remaining[..width];
                    remaining = remaining[width..];
                }

                if (line.Length > 0 && line.Length + remaining.Length + 1 > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(remaining);
            }

            yield return line.ToString();
        }
    }

    private static string Escape(string? value)
    {
        var result = new StringBuilder(value?.Length ?? 0);

        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '(':
                case ')':
                case '\\':
                    result.Append('\\').Append(ch);
                    break;
                case '\r':
                case '\n':
                case '\t':
                    result.Append(' ');
                    break;
                default:
                    result.Append(ch is < ' ' or > '\u00ff' ? '?' : ch);
                    break;
            }
        }

        return result.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>
/// DOCX writer using the Open Packaging Conventions directly. One document part, simple
/// paragraphs; no external library and no template file.
/// </summary>
public sealed class DocxReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Docx;
    public string FileExtension => ".docx";

    public string Render(ReportContext context) =>
        throw new NotSupportedException("DOCX is a binary format; use RenderBytes.");

    public byte[] RenderBytes(ReportContext context)
    {
        var body = new StringBuilder();
        var assessment = context.Assessment;
        var scores = assessment.Scores;

        void Para(string text, bool bold = false, int halfPoints = 20, string color = "16213B")
        {
            body.Append("<w:p><w:pPr><w:spacing w:after=\"80\"/></w:pPr><w:r><w:rPr>");
            if (bold) body.Append("<w:b/>");
            body.Append(FormattableString.Invariant($"<w:sz w:val=\"{halfPoints}\"/><w:color w:val=\"{color}\"/>"));
            body.Append("</w:rPr><w:t xml:space=\"preserve\">").Append(Xml(text)).Append("</w:t></w:r></w:p>");
        }

        Para("Purview Governance Copilot", bold: true, halfPoints: 40);
        Para($"Governance report — {context.OrganizationName}", bold: true, halfPoints: 28);
        Para($"Generated {context.GeneratedUtc:yyyy-MM-dd HH:mm} UTC by {context.GeneratedBy}", halfPoints: 18, color: "5B6B85");
        Para($"Tenant {context.Tenant.TenantName} · Assessment {assessment.Id}", halfPoints: 18, color: "5B6B85");

        Para("Scores", bold: true, halfPoints: 26);
        Para($"Overall {scores.Overall:0.#} · Maturity {scores.MaturityLevel} · Risk exposure {scores.Risk:0.#}");
        Para($"Security {scores.Security:0.#} · Governance {scores.Governance:0.#} · Compliance {scores.Compliance:0.#}");
        Para($"AI governance {scores.AiGovernance:0.#} · Copilot readiness {scores.CopilotReadiness:0.#}");
        Para($"Findings {assessment.Findings.Count} · Recommendations {assessment.Recommendations.Count}");

        Para("Module scores", bold: true, halfPoints: 26);
        foreach (var module in scores.Modules.OrderByDescending(m => m.Assessed))
        {
            Para($"{module.Module}: {module.Score:0.#} — {module.Assessed} assessed, {module.Passed} pass, {module.Warnings} warn, {module.Failed} fail", halfPoints: 18);
        }

        Para("Framework coverage", bold: true, halfPoints: 26);
        if (context.Frameworks.Count == 0)
        {
            Para("No framework-mapped findings in this assessment.", halfPoints: 18);
        }
        else
        {
            foreach (var framework in context.Frameworks)
            {
                Para($"{framework.Framework}: {framework.Percent:0.#}% ({framework.CoveredControls}/{framework.TotalControls})", bold: true, halfPoints: 20);
                if (framework.Gaps.Count > 0)
                {
                    Para($"Gaps: {string.Join("; ", framework.Gaps.Take(8))}", halfPoints: 18);
                }
            }
        }

        Para("Findings", bold: true, halfPoints: 26);
        foreach (var finding in assessment.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Id).Take(120))
        {
            Para($"[{finding.Status}] {finding.Id} — {finding.Title}", bold: true, halfPoints: 19);
            Para($"Evidence: {finding.Evidence}", halfPoints: 17);
            if (!string.IsNullOrWhiteSpace(finding.Remediation))
            {
                Para($"Remediation: {finding.Remediation}", halfPoints: 17);
            }
        }

        Para("Recommendations", bold: true, halfPoints: 26);
        foreach (var recommendation in assessment.Recommendations.Take(60))
        {
            Para($"{recommendation.Priority} · {recommendation.Id} — {recommendation.Title}", halfPoints: 18);
            Para($"Licences: {string.Join(", ", recommendation.RequiredLicenses)} · Change risk: {recommendation.ChangeRisk}", halfPoints: 16);
        }

        var document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            body +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\"/></w:sectPr>" +
            "</w:body></w:document>";

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";

        const string rootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>";

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypes);
            AddEntry(archive, "_rels/.rels", rootRels);
            AddEntry(archive, "word/document.xml", document);
        }

        return stream.ToArray();
    }

    internal static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    internal static string Xml(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}

/// <summary>
/// XLSX writer with three worksheets (Scores, Findings, Frameworks) built directly on the
/// SpreadsheetML package format. Inline strings avoid a shared-string table.
/// </summary>
public sealed class XlsxReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Xlsx;
    public string FileExtension => ".xlsx";

    public string Render(ReportContext context) =>
        throw new NotSupportedException("XLSX is a binary format; use RenderBytes.");

    public byte[] RenderBytes(ReportContext context)
    {
        var assessment = context.Assessment;
        var scores = assessment.Scores;

        var scoreRows = new List<object?[]>
        {
            new object?[] { "Metric", "Value" },
            new object?[] { "Overall", scores.Overall },
            new object?[] { "Security", scores.Security },
            new object?[] { "Governance", scores.Governance },
            new object?[] { "Compliance", scores.Compliance },
            new object?[] { "Risk exposure", scores.Risk },
            new object?[] { "AI governance", scores.AiGovernance },
            new object?[] { "Copilot readiness", scores.CopilotReadiness },
            new object?[] { "Maturity", scores.MaturityLevel.ToString() },
            Array.Empty<object?>(),
            new object?[] { "Module", "Score", "Assessed", "Pass", "Warn", "Fail" }
        };

        scoreRows.AddRange(scores.Modules
            .OrderByDescending(m => m.Assessed)
            .Select(m => new object?[] { m.Module.ToString(), m.Score, m.Assessed, m.Passed, m.Warnings, m.Failed }));

        var findingRows = new List<object?[]>
        {
            new object?[] { "Id", "Module", "Title", "Status", "Severity", "Evidence", "Remediation", "Frameworks" }
        };

        findingRows.AddRange(assessment.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Id)
            .Select(f => new object?[]
            {
                f.Id, f.Module.ToString(), f.Title, f.Status.ToString(), f.Severity.ToString(),
                f.Evidence, f.Remediation, string.Join("; ", f.FrameworkRefs)
            }));

        var frameworkRows = new List<object?[]>
        {
            new object?[] { "Framework", "Coverage %", "Covered", "Total", "Gaps" }
        };

        frameworkRows.AddRange(context.Frameworks.Select(f => new object?[]
        {
            f.Framework, f.Percent, f.CoveredControls, f.TotalControls, string.Join("; ", f.Gaps)
        }));

        var contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        const string rootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        const string workbook =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets>" +
            "<sheet name=\"Scores\" sheetId=\"1\" r:id=\"rId1\"/>" +
            "<sheet name=\"Findings\" sheetId=\"2\" r:id=\"rId2\"/>" +
            "<sheet name=\"Frameworks\" sheetId=\"3\" r:id=\"rId3\"/>" +
            "</sheets></workbook>";

        const string workbookRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/>" +
            "</Relationships>";

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            DocxReportExporter.AddEntry(archive, "[Content_Types].xml", contentTypes);
            DocxReportExporter.AddEntry(archive, "_rels/.rels", rootRels);
            DocxReportExporter.AddEntry(archive, "xl/workbook.xml", workbook);
            DocxReportExporter.AddEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            DocxReportExporter.AddEntry(archive, "xl/worksheets/sheet1.xml", Sheet(scoreRows));
            DocxReportExporter.AddEntry(archive, "xl/worksheets/sheet2.xml", Sheet(findingRows));
            DocxReportExporter.AddEntry(archive, "xl/worksheets/sheet3.xml", Sheet(frameworkRows));
        }

        return stream.ToArray();
    }

    private static string Sheet(IEnumerable<object?[]> rows)
    {
        var sheet = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        var rowNumber = 0;

        foreach (var row in rows)
        {
            rowNumber++;
            sheet.Append(FormattableString.Invariant($"<row r=\"{rowNumber}\">"));

            var column = 0;
            foreach (var value in row)
            {
                column++;
                var reference = Column(column) + rowNumber.ToString(CultureInfo.InvariantCulture);

                switch (value)
                {
                    case null:
                        break;
                    case double number:
                        sheet.Append(FormattableString.Invariant($"<c r=\"{reference}\"><v>{number.ToString("0.####", CultureInfo.InvariantCulture)}</v></c>"));
                        break;
                    case int integer:
                        sheet.Append(FormattableString.Invariant($"<c r=\"{reference}\"><v>{integer}</v></c>"));
                        break;
                    default:
                        sheet.Append(FormattableString.Invariant($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{DocxReportExporter.Xml(value.ToString())}</t></is></c>"));
                        break;
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        return sheet.ToString();
    }

    private static string Column(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }

        return name;
    }
}
