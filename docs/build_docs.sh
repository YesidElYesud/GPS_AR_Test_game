#!/bin/bash

# ============================================
# CONFIGURACIÓN
# ============================================

OUTPUT="Documentacion_Unity_SATC.pdf"
TITLE="Documentación Técnica - Proyecto Unity"
PROJECT="Experiencia educativa AR/VR sobre alertas tempranas comunitarias."
AUTHOR="Equipo DEV Uni. Pascual Bravo"
DATE=$(date +"%d de %B de %Y")

# Archivos en orden (importante para la narrativa)
FILES=(
  01_OVERVIEW.md
  07_DESPLIEGUE.md
  02_ARQUITECTURA.md
  03_SCRIPTS.md
  04_CREAR_ESCENA.md
  05_FLUJO_TRABAJO.md
  06_LIMITACIONES.md
)

# ============================================
# VERIFICAR DEPENDENCIAS
# ============================================

if ! command -v pandoc &> /dev/null
then
    echo "❌ Pandoc no está instalado"
    exit 1
fi

if ! command -v xelatex &> /dev/null
then
    echo "❌ XeLaTeX no está instalado (texlive)"
    exit 1
fi

# ============================================
# GENERAR PDF
# ============================================

echo "🚀 Generando documentación..."

pandoc "${FILES[@]}" \
  --pdf-engine=xelatex \
  -o "$OUTPUT" \
  --toc \
  --toc-depth=3 \
  --number-sections \
  -V documentclass=report \
  -V lang=es \
  -V geometry:margin=2.5cm \
  -V fontsize=11pt \
  -V mainfont="DejaVu Serif" \
  -V sansfont="DejaVu Sans" \
  -V monofont="DejaVu Sans Mono" \
  -V title="$TITLE" \
  -V subtitle="$PROJECT"\
  -V author="$AUTHOR" \
  -V date="$DATE"

# ============================================
# RESULTADO
# ============================================

if [ $? -eq 0 ]; then
    echo "✅ PDF generado correctamente: $OUTPUT"
else
    echo "❌ Error al generar el PDF"
fi
