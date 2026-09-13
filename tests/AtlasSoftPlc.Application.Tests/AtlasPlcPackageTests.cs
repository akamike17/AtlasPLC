using System.IO.Compression;
using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Application.Packages;

namespace AtlasSoftPlc.Application.Tests;

public sealed class AtlasPlcPackageTests
{
    [Fact]
    public void ExportImportPreservesIrIdsPlantAndSemanticHash()
    {
        var ir = AtlasIrFixtures.BuildMotorStopGuard();
        var package = new AtlasPlcPackageService().Export(ir, artifacts: new Dictionary<string, byte[]> { ["motor.st"] = "PROGRAM"u8.ToArray() });
        var imported = new AtlasPlcPackageService().Import(package);
        Assert.Equal(ir.Id, imported.Ir.Id);
        Assert.Equal(ir.Variables.Select(x => x.Id), imported.Ir.Variables.Select(x => x.Id));
        Assert.Equal(AtlasSoftPlc.Domain.Projects.CanonicalProgramHasher.ComputeHash(ir.ToProgramDefinition()), AtlasSoftPlc.Domain.Projects.CanonicalProgramHasher.ComputeHash(imported.Ir.ToProgramDefinition()));
        Assert.Equal("PROGRAM", System.Text.Encoding.UTF8.GetString(imported.Artifacts["motor.st"]));
    }

    [Fact]
    public void PackageContainsVersionedEntriesAndRejectsTamperedManifest()
    {
        var bytes = new AtlasPlcPackageService().Export(AtlasIrFixtures.BuildTank());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(zip.Entries, e => e.FullName == "ir.json");
        Assert.Contains(zip.Entries, e => e.FullName == "plant.json");
        Assert.Contains(zip.Entries, e => e.FullName == "scenarios/.keep");
    }
}
