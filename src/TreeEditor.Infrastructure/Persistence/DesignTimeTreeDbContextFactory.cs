using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>Creates the context for <c>dotnet ef</c> commands, which never connect to a database here.</summary>
internal sealed class DesignTimeTreeDbContextFactory : IDesignTimeDbContextFactory<TreeDbContext>
{
    /// <inheritdoc />
    public TreeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TreeDbContext>()
            .UseNpgsql("Host=localhost;Database=tree_editor_design")
            .Options);
}
