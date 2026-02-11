﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomationAPI.Migrations
{
    /// <inheritdoc />
    public partial class FixAutomationStatusFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstadoAutomatizacion",
                table: "Registros",
                type: "text",
                nullable: false,
                defaultValue: "PENDIENTE");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaActualizacion",
                table: "Registros",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UltimoErrorAutomatizacion",
                table: "Registros",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstadoAutomatizacion",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "FechaActualizacion",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "UltimoErrorAutomatizacion",
                table: "Registros");
        }
    }
}
