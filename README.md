# MojarraTube

App nativa de Windows (C# / WPF, .NET 8) para ver YouTube sin navegador, descargar videos o solo el audio
(de YouTube y de cientos de páginas más) y reproducir tus archivos con un reproductor propio.

## Qué hace
- **Ver YouTube**: inicio por temas, búsqueda con sugerencias, canales, listas, capítulos, descripción con enlaces y marcas de tiempo.
- **Suscripciones locales** (sin cuenta, por RSS), historial, "Ver más tarde" y "Seguir viendo" (recuerda la posición).
- **Reproductor propio** (LibVLC): calidad, velocidad, pistas de audio, subtítulos, captura, mini reproductor, pantalla completa,
  atajos tipo YouTube (K/espacio, J/L, flechas, F, M, Shift+N/P, 0–9…).
- **Descargas**: video en varias calidades (hasta 4K) o solo audio (MP3 320/192, M4A, Opus), listas completas,
  pausar/reanudar, varias a la vez. Pega cualquier enlace (no solo YouTube); detecta enlaces copiados.
- **Biblioteca**: tus carpetas de videos y música, abrir archivos con doble clic (asociación "Abrir con").
- Tema oscuro/claro/sistema, diseño responsivo (menú completo, compacto o flotante según el ancho).

## Componentes
En el primer arranque descarga a `%LOCALAPPDATA%\MojarraTube\componentes`: **yt-dlp** (se actualiza solo cada semana),
**Deno** (lo necesita yt-dlp para YouTube) y **FFmpeg** (unir video+audio y convertir audio).

## Datos
`Documentos\MojarraTube\` — ajustes, historial, suscripciones, descargas (JSON).
Descargas por defecto: `Videos\MojarraTube` y `Música\MojarraTube`.

## Compilar
```
scripts\compilar.ps1          # Windows: dotnet publish + NSIS → dist\MojarraTube-Setup-<versión>.exe
./scripts/compilar.sh         # Linux (con el SDK WindowsDesktop y makensis)
dotnet run --project tests/Pruebas   # pruebas de la lógica (formatos, parseo de yt-dlp, textos)
```
