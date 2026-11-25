using System.IO.Compression;
using System.Text;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Demo;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class OfficeReportTests
{
    private static ReportContext Context()
    {
        var finding = new Finding(
            "AUD-01", ModuleKey.Audit, "Unified audit log ingestion is enabled",
            FindingStatus.Fail, Severity.Critical,
            "Unified audit log ingestion is disabled.",
            "Enable audit in the Purview portal.",
            new[] { "ISO 27001 A.8.15" });

        var scores = new ScoreSet(
            Security: 70, Governance: 65, Compliance: 60, Maturity: 58,
            MaturityLevel.Defined, Risk: 30, AiGovernance: 40, CopilotReadiness: 50,
            Overall: 62,
            Modules: new[] { new ModuleScore(ModuleKey.Audit, 60, 2, 1, 0, 1) });

        var assessment = new AssessmentResult(
            Guid.NewGuid(), "tenant", new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero),
            "AssessmentOnly", scores, new[] { finding }, Array.Empty<Recommendation>(), 49);

        var frameworks = new[]
        {
            new FrameworkCoverage("ISO 27001", 50, 2, 1, new[] { "AUD-01 Unified audit log ingestion is enabled" })
        };

        return new ReportContext(
            DemoTenant.Create(), assessment, "Contoso", "tester",
            new DateTimeOffset(2026, 11, 25, 11, 0, 0, TimeSpan.Zero), frameworks);
    }

    [Fact]
    public void Pdf_ProducesAWellFormedDocumentWithTheReportText()
    {
        var bytes = new PdfReportExporter().RenderBytes(Context());
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("Purview Governance Copilot", text, StringComparison.Ordinal);
        Assert.Contains("Contoso", text, StringComparison.Ordinal);
        Assert.Contains("AUD-01", text, StringComparison.Ordinal);
        Assert.Contains("startxref", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Page", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_EscapesParenthesesInContent()
    {
        var context = Context() with
        {
            OrganizationName = "Contoso (EMEA) \\ Holdings"
        };

        var text = Encoding.Latin1.GetString(new PdfReportExporter().RenderBytes(context));

        Assert.Contains(@"Contoso \(EMEA\) \\ Holdings", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Docx_IsAValidPackageWithDocumentText()
    {
        var bytes = new DocxReportExporter().RenderBytes(Context());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        var document = archive.GetEntry("word/document.xml");
        Assert.NotNull(document);

        using var reader = new StreamReader(document!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("Contoso", xml, StringComparison.Ordinal);
        Assert.Contains("AUD-01", xml, StringComparison.Ordinal);
        Assert.Contains("<w:document", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_HasThreeSheetsWithScoredAndFindingRows()
    {
        var bytes = new XlsxReportExporter().RenderBytes(Context());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));

        string Read(string name)
        {
            var entry = archive.GetEntry(name);
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            return reader.ReadToEnd();
        }

        Assert.Contains("Scores", Read("xl/workbook.xml"), StringComparison.Ordinal);
        Assert.Contains("Overall", Read("xl/worksheets/sheet1.xml"), StringComparison.Ordinal);
        Assert.Contains("AUD-01", Read("xl/worksheets/sheet2.xml"), StringComparison.Ordinal);
        Assert.Contains("ISO 27001", Read("xl/worksheets/sheet3.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Exporters_DeclareDistinctFormatsAndExtensions()
    {
        IReportExporter[] exporters =
        {
            new PdfReportExporter(),
            new DocxReportExporter(),
            new XlsxReportExporter()
        };

        Assert.Equal(exporters.Length, exporters.Select(e => e.Format).Distinct().Count());
        Assert.Equal(new[] { ".pdf", ".docx", ".xlsx" }, exporters.Select(e => e.FileExtension));
    }
}
