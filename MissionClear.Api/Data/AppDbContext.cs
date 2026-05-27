using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
