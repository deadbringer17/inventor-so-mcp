using System;
using System.IO;
using Bimwright.Ipt.Shared.Infrastructure;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// The workspace boundary is what keeps in-place saving away from user documents, so the path and
/// name rules are pinned here without Inventor.
/// </summary>
public sealed class WorkspaceDocumentPolicyTests
{
    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Theory]
    [InlineData("part", ".ipt")]
    [InlineData("assembly", ".iam")]
    [InlineData("sheet_metal", ".ipt")]
    public void Extension_maps_supported_kinds(string kind, string extension)
        => Assert.Equal(extension, WorkspaceDocumentPolicy.Extension(kind));

    [Theory]
    [InlineData("drawing")]
    [InlineData("presentation")]
    [InlineData("sheetmetal")]
    [InlineData("Sheet_Metal")]
    [InlineData("")]
    [InlineData(null)]
    public void Extension_rejects_other_kinds(string? kind)
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.Extension(kind));

    [Theory]
    [InlineData("Bracket")]
    [InlineData("bracket-01")]
    [InlineData("Bracket plate_2")]
    [InlineData("7")]
    public void ValidateName_accepts_plain_names(string name)
        => Assert.Equal(name, WorkspaceDocumentPolicy.ValidateName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("trailing.")]
    [InlineData("_leading")]
    [InlineData("../escape")]
    [InlineData("sub/dir")]
    [InlineData("back\\slash")]
    [InlineData("colon:name")]
    [InlineData("star*")]
    [InlineData("CON")]
    [InlineData("com1")]
    public void ValidateName_rejects_unsafe_names(string? name)
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.ValidateName(name));

    [Fact]
    public void ValidateName_rejects_over_sixty_characters()
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.ValidateName(new string('a', 61)));

    [Fact]
    public void IsInside_accepts_only_real_children_of_the_root()
    {
        string root = TempRoot();
        Assert.True(WorkspaceDocumentPolicy.IsInside(root, Path.Combine(root, "Bracket.ipt")));
        Assert.True(WorkspaceDocumentPolicy.IsInside(root, Path.Combine(root, "nested", "Bracket.ipt")));
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, root));
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, Path.Combine(root, "..", "Bracket.ipt")));
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, Path.Combine(Path.GetTempPath(), "Bracket.ipt")));
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, ""));
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, null));
    }

    [Fact]
    public void IsInside_ignores_case_and_separator_style()
    {
        string root = TempRoot();
        Assert.True(WorkspaceDocumentPolicy.IsInside(root.ToUpperInvariant(), Path.Combine(root, "Bracket.ipt")));
        Assert.True(WorkspaceDocumentPolicy.IsInside(root + Path.DirectorySeparatorChar, Path.Combine(root, "Bracket.ipt")));
    }

    [Fact]
    public void IsInside_rejects_a_sibling_directory_sharing_the_root_prefix()
    {
        string root = TempRoot();
        Assert.False(WorkspaceDocumentPolicy.IsInside(root, root + "-other" + Path.DirectorySeparatorChar + "Bracket.ipt"));
    }

    [Fact]
    public void NewPath_builds_a_flat_workspace_path()
    {
        string root = TempRoot();
        Assert.Equal(Path.Combine(root, "Bracket.ipt"), WorkspaceDocumentPolicy.NewPath(root, "Bracket", "part"));
        Assert.Equal(Path.Combine(root, "Frame.iam"), WorkspaceDocumentPolicy.NewPath(root, "Frame", "assembly"));
    }

    [Fact]
    public void NewPath_never_overwrites_an_existing_document()
    {
        string root = TempRoot();
        File.WriteAllText(Path.Combine(root, "Bracket.ipt"), "existing");
        Assert.Throws<IOException>(() => WorkspaceDocumentPolicy.NewPath(root, "Bracket", "part"));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(root, "Bracket.ipt")));
    }

    [Theory]
    [InlineData(".idw")]
    [InlineData(".dwg")]
    [InlineData(".IPT")]
    public void NewPathWithExtension_accepts_every_managed_extension(string extension)
    {
        string root = TempRoot();
        Assert.Equal(Path.Combine(root, "Sheet" + extension.ToLowerInvariant()),
            WorkspaceDocumentPolicy.NewPathWithExtension(root, "Sheet", extension));
    }

    [Theory]
    [InlineData(".step")]
    [InlineData(".exe")]
    [InlineData("")]
    [InlineData(null)]
    public void NewPathWithExtension_rejects_other_extensions(string? extension)
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.NewPathWithExtension(TempRoot(), "Sheet", extension));

    [Fact]
    public void ExistingPath_resolves_only_real_workspace_documents()
    {
        string root = TempRoot();
        string file = Path.Combine(root, "Bracket.ipt");
        File.WriteAllText(file, "cad");
        Assert.Equal(file, WorkspaceDocumentPolicy.ExistingPath(root, "Bracket.ipt"));
        Assert.Throws<FileNotFoundException>(() => WorkspaceDocumentPolicy.ExistingPath(root, "Missing.ipt"));
    }

    [Theory]
    [InlineData("Bracket")]
    [InlineData("Bracket.step")]
    [InlineData("../outside/Bracket.ipt")]
    [InlineData("sub/Bracket.ipt")]
    [InlineData(@"C:\elsewhere\Bracket.ipt")]
    [InlineData("")]
    [InlineData(null)]
    public void ExistingPath_refuses_paths_and_unmanaged_files(string? file)
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.ExistingPath(TempRoot(), file));

    [Fact]
    public void IsManagedExtension_covers_cad_documents_only()
    {
        Assert.True(WorkspaceDocumentPolicy.IsManagedExtension("a.ipt"));
        Assert.True(WorkspaceDocumentPolicy.IsManagedExtension("a.IAM"));
        Assert.True(WorkspaceDocumentPolicy.IsManagedExtension("a.idw"));
        Assert.True(WorkspaceDocumentPolicy.IsManagedExtension("a.dwg"));
        Assert.False(WorkspaceDocumentPolicy.IsManagedExtension("a.step"));
        Assert.False(WorkspaceDocumentPolicy.IsManagedExtension("a.ipt.bak"));
        Assert.False(WorkspaceDocumentPolicy.IsManagedExtension("a"));
    }

    [Fact]
    public void Default_root_stays_under_the_host_owned_local_application_data()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "workspace");
        Assert.Equal(expected, WorkspaceDocumentPolicy.DefaultRoot());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_falls_back_to_the_default_only_when_unset(string? configured)
        => Assert.Equal(WorkspaceDocumentPolicy.DefaultRoot(), WorkspaceDocumentPolicy.Resolve(configured));

    [Fact]
    public void Resolve_accepts_a_project_directory_and_normalizes_it()
    {
        string configured = Path.Combine(Path.GetTempPath(), "inventor-so-project", "workspace");
        Assert.Equal(Path.GetFullPath(configured), WorkspaceDocumentPolicy.Resolve(configured));
        Assert.Equal(Path.GetFullPath(configured), WorkspaceDocumentPolicy.Resolve(configured + Path.DirectorySeparatorChar));
        Assert.Equal(Path.GetFullPath(configured), WorkspaceDocumentPolicy.Resolve("  " + configured + "  "));
    }

    [Theory]
    [InlineData("relative\\path")]
    [InlineData("C:")]
    [InlineData("C:\\")]
    public void Resolve_rejects_relative_paths_and_drive_roots(string configured)
        => Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.Resolve(configured));

    [Fact]
    public void Resolve_rejects_windows_and_program_files_trees()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.Resolve(windows));
        Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.Resolve(Path.Combine(windows, "System32", "cad")));
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Throws<ArgumentException>(() => WorkspaceDocumentPolicy.Resolve(Path.Combine(programs, "cad")));
    }

    [Fact]
    public void Root_reads_the_configured_variable()
    {
        string configured = Path.Combine(Path.GetTempPath(), "inventor-so-root-variable");
        string? prior = Environment.GetEnvironmentVariable(WorkspaceDocumentPolicy.RootVariable);
        try
        {
            Environment.SetEnvironmentVariable(WorkspaceDocumentPolicy.RootVariable, configured);
            Assert.Equal(Path.GetFullPath(configured), WorkspaceDocumentPolicy.Root());
        }
        finally { Environment.SetEnvironmentVariable(WorkspaceDocumentPolicy.RootVariable, prior); }
    }
}
