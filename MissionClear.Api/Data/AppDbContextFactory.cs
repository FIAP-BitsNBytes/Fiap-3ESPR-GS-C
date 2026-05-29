using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MissionClear.Api.Data;

/// <summary>
/// Permite rodar 'dotnet ef migrations add' mesmo sem o Aspire AppHost ativo.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        // Versão do servidor — ajuste conforme necessário p/ o MySQL local
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 0));
        
        // String dummy p/ geração de código — o AppHost sobrescreve isso em runtime
        const string connectionString = "Server=localhost;Port=3306;Database=missionclear;User=root;Password=MissionClear_Dev_2025!;";

        optionsBuilder.UseMySql(connectionString, serverVersion);

        return new AppDbContext(optionsBuilder.Options);
    }
}
