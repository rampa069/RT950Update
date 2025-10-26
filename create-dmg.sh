#!/bin/bash

# Script para crear DMG de RT950Update para macOS

set -e

# Configuración
APP_NAME="RT950Update"
VERSION="1.0.0"
BUNDLE_ID="com.rt950.update"
DMG_NAME="${APP_NAME}-${VERSION}"
BUILD_DIR="./build"
APP_DIR="${BUILD_DIR}/${APP_NAME}.app"
DMG_TEMP_DIR="${BUILD_DIR}/dmg_temp"

# Modo de compilación: "universal", "arm64", o "x64"
BUILD_MODE="${1:-universal}"

if [ "$BUILD_MODE" != "universal" ] && [ "$BUILD_MODE" != "arm64" ] && [ "$BUILD_MODE" != "x64" ]; then
    echo "Modo no válido. Usa 'universal', 'arm64' o 'x64'"
    exit 1
fi

echo "================================================"
echo "Creando DMG para ${APP_NAME}"
echo "Modo: ${BUILD_MODE}"
echo "================================================"

# Limpiar build anterior
echo "🧹 Limpiando builds anteriores..."
rm -rf "${BUILD_DIR}"
mkdir -p "${BUILD_DIR}"
mkdir -p "${DMG_TEMP_DIR}"

# Crear estructura del bundle .app
echo "📦 Creando bundle .app..."
mkdir -p "${APP_DIR}/Contents/MacOS"
mkdir -p "${APP_DIR}/Contents/Resources"

if [ "$BUILD_MODE" == "universal" ]; then
    echo "🔨 Compilando para arm64..."
    dotnet publish -c Release -r "osx-arm64" --self-contained true -o "${BUILD_DIR}/publish-arm64" -p:PublishSingleFile=false

    echo "🔨 Compilando para x64..."
    dotnet publish -c Release -r "osx-x64" --self-contained true -o "${BUILD_DIR}/publish-x64" -p:PublishSingleFile=false

    echo "🔗 Creando binarios universales con lipo..."

    # Copiar la estructura base de arm64
    cp -r "${BUILD_DIR}/publish-arm64/"* "${APP_DIR}/Contents/MacOS/"

    # Crear binarios universales para los archivos nativos
    for file in "${BUILD_DIR}/publish-arm64/"*.dylib "${BUILD_DIR}/publish-arm64/${APP_NAME}"; do
        if [ -f "$file" ]; then
            filename=$(basename "$file")
            arm64_file="${BUILD_DIR}/publish-arm64/${filename}"
            x64_file="${BUILD_DIR}/publish-x64/${filename}"
            output_file="${APP_DIR}/Contents/MacOS/${filename}"

            if [ -f "$x64_file" ]; then
                # Verificar si son binarios Mach-O (nativos)
                if file "$arm64_file" | grep -q "Mach-O"; then
                    # Obtener las arquitecturas de cada archivo
                    arm64_archs=$(lipo -info "$arm64_file" 2>/dev/null | grep -o "arm64\|x86_64" || echo "")
                    x64_archs=$(lipo -info "$x64_file" 2>/dev/null | grep -o "arm64\|x86_64" || echo "")

                    # Si ambos archivos tienen las mismas arquitecturas, usar solo uno
                    if [ "$arm64_archs" == "$x64_archs" ]; then
                        echo "  ℹ️  ${filename} ya contiene ambas arquitecturas, usando arm64 version"
                        cp "$arm64_file" "$output_file"
                    # Si uno ya es universal, usarlo
                    elif echo "$arm64_archs" | grep -q "arm64" && echo "$arm64_archs" | grep -q "x86_64"; then
                        echo "  ℹ️  ${filename} (arm64 build) ya es universal"
                        cp "$arm64_file" "$output_file"
                    elif echo "$x64_archs" | grep -q "arm64" && echo "$x64_archs" | grep -q "x86_64"; then
                        echo "  ℹ️  ${filename} (x64 build) ya es universal"
                        cp "$x64_file" "$output_file"
                    # Si tienen diferentes arquitecturas, combinarlas
                    else
                        echo "  Combinando ${filename}..."
                        lipo -create "$arm64_file" "$x64_file" -output "$output_file" 2>/dev/null || {
                            echo "  ⚠️  No se pudo combinar ${filename}, usando versión arm64"
                            cp "$arm64_file" "$output_file"
                        }
                    fi
                fi
            fi
        fi
    done
else
    RUNTIME="osx-${BUILD_MODE}"
    echo "🔨 Compilando aplicación para ${RUNTIME}..."
    dotnet publish -c Release -r "${RUNTIME}" --self-contained true -o "${BUILD_DIR}/publish" -p:PublishSingleFile=false

    # Copiar ejecutable y dependencias
    cp -r "${BUILD_DIR}/publish/"* "${APP_DIR}/Contents/MacOS/"
fi

# Hacer ejecutable el archivo principal
chmod +x "${APP_DIR}/Contents/MacOS/${APP_NAME}"

# Crear Info.plist
echo "📝 Creando Info.plist..."
cat > "${APP_DIR}/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIconFile</key>
    <string>icon.icns</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
EOF

# Si existe un icono, copiarlo (opcional)
if [ -f "Assets/icon.icns" ]; then
    echo "🎨 Copiando icono..."
    cp "Assets/icon.icns" "${APP_DIR}/Contents/Resources/"
fi

# Copiar el bundle al directorio temporal del DMG
echo "📋 Preparando contenido del DMG..."
cp -r "${APP_DIR}" "${DMG_TEMP_DIR}/"

# Crear link simbólico a Applications
ln -s /Applications "${DMG_TEMP_DIR}/Applications"

# Crear DMG temporal
echo "💿 Creando DMG..."
DMG_TEMP="${BUILD_DIR}/${DMG_NAME}-temp.dmg"
DMG_FINAL="${BUILD_DIR}/${DMG_NAME}.dmg"

# Crear DMG temporal de lectura-escritura
hdiutil create -srcfolder "${DMG_TEMP_DIR}" -volname "${APP_NAME}" -fs HFS+ \
    -fsargs "-c c=64,a=16,e=16" -format UDRW -size 200m "${DMG_TEMP}"

# Montar el DMG temporal
echo "🔧 Configurando DMG..."
MOUNT_OUTPUT=$(hdiutil attach -readwrite -noverify -noautoopen "${DMG_TEMP}" 2>&1)
MOUNT_DIR=$(echo "$MOUNT_OUTPUT" | grep -o '/Volumes/[^[:space:]]*' | head -1)

if [ -z "$MOUNT_DIR" ]; then
    echo "⚠️  No se pudo montar el DMG. Continuando sin configuración de vista..."
else
    echo "   Montado en: ${MOUNT_DIR}"

    # Configurar la vista del Finder (opcional)
    sleep 2
    echo '
       tell application "Finder"
         tell disk "'${APP_NAME}'"
               open
               set current view of container window to icon view
               set toolbar visible of container window to false
               set statusbar visible of container window to false
               set the bounds of container window to {400, 100, 900, 400}
               set viewOptions to the icon view options of container window
               set arrangement of viewOptions to not arranged
               set icon size of viewOptions to 72
               set position of item "'${APP_NAME}'.app" of container window to {100, 100}
               set position of item "Applications" of container window to {375, 100}
               update without registering applications
               delay 2
               close
         end tell
       end tell
    ' | osascript 2>/dev/null || echo "   ℹ️  No se pudo configurar la vista (opcional)"

    # Desmontar
    sync
    sleep 1
    hdiutil detach "${MOUNT_DIR}" -force || echo "   ℹ️  Advertencia: No se pudo desmontar automáticamente"
fi

# Convertir a DMG comprimido final
echo "🗜️  Comprimiendo DMG..."
rm -f "${DMG_FINAL}"
hdiutil convert "${DMG_TEMP}" -format UDZO -imagekey zlib-level=9 -o "${DMG_FINAL}"

# Limpiar archivos temporales
rm -f "${DMG_TEMP}"
rm -rf "${DMG_TEMP_DIR}"

echo "✅ DMG creado exitosamente: ${DMG_FINAL}"
echo ""
echo "Para instalar:"
echo "  1. Abre ${DMG_NAME}.dmg"
echo "  2. Arrastra ${APP_NAME}.app a la carpeta Applications"
echo ""
echo "================================================"
