using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutomationAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Registros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nit = table.Column<string>(type: "text", nullable: false),
                    Empresa = table.Column<string>(type: "text", nullable: false),
                    Ciudad = table.Column<string>(type: "text", nullable: false),
                    Cliente = table.Column<string>(type: "text", nullable: false),
                    Celular = table.Column<string>(type: "text", nullable: false),
                    Correo = table.Column<string>(type: "text", nullable: false),
                    TipoCliente = table.Column<string>(type: "text", nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registros", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Registros");
        }
    }
}
