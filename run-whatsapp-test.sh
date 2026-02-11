#!/bin/bash

# Script para ejecutar el test de WhatsApp

cd "/home/zoyeras/Desktop/Projectos trabajo/AutoHJR360/Backend/AutomationAPI"

echo "🔨 Compilando TestWhatsAppOnly..."
dotnet build -c Debug

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Compilación exitosa"
    echo ""
    echo "🚀 Ejecutando prueba de WhatsApp..."
    echo ""
    dotnet run --project . -- --test-whatsapp
else
    echo "❌ Error en compilación"
    exit 1
fi

