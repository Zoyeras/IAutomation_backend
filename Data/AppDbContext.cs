using Microsoft.EntityFrameworkCore;
using AutomationAPI.Models;

namespace AutomationAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<Registro> Registros => Set<Registro>();
}