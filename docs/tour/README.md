# Tour source / Fuentes del recorrido

`index.html` is a nine-scene, 77-second storyboard in English and Spanish. It moves from the animated notch states (resting mascot, compact pill, music, lyrics and system) into two full-screen views captured from the current desktop app: the widget gallery and display settings. The video uses measured camera movement and a brief edge-light sweep to connect scenes; the notch animation follows real-time frame pacing at 30 fps.

The simulator uses sample data; it is not a live recording. The widget gallery and display settings are real WPF captures from the current source, generated with demo data. `tour-ui-widgets-{es,en}.png` and `tour-ui-display-{es,en}.png` are their source images.

The soundtrack in `../audio/tour-soundtrack.m4a` is shared by both languages and copied into the videos without audio re-encoding. SHA-256: `ca75fa81af140e7a531eceb8a1859ab19690b89e3edf8d64782ad742518eae89`.

Install Python with Pillow and imageio-ffmpeg, Node.js, Chrome and the .NET 10 SDK. Puppeteer is already available through `obj/tour-tooling` in this workspace; to prepare it in a clean checkout:

```powershell
python -m pip install --target obj/tour-tooling Pillow imageio-ffmpeg==0.6.0
npm install --prefix obj/tour-tooling puppeteer-core@25.12.0
python docs/generate-tours.py --capture
```

Refresh current WPF captures, review scenes, encode both languages and verify the files:

```powershell
python docs/generate-tours.py --capture
python docs/generate-tours.py --stills 2,10,19,27,38,47,57,68,75
python docs/generate-tours.py
python docs/tour/verify.py
```

Review frames are saved in `obj/tour-work/review/`. Final MP4s and posters replace the tracked files only after a successful encode. `--lang es|en` renders one language; `--chrome`, `--node` and `--dotnet` accept custom runtime paths.

The site reads `../video/isTargetSleeping-tour-{es,en}.mp4` and `../images/tour-{es,en}.jpg`. README pages link to the deployed tour. Keep the MP4 in `docs/video/` as the shared source for the website and download.

---

`index.html` presenta un guion de nueve escenas y 77 segundos, en español e inglés. Recorre los estados animados del notch (mascota en reposo, pastilla compacta, música, letras y sistema) y luego muestra a pantalla completa dos vistas capturadas de la app actual: la galería de widgets y los ajustes de pantalla. El movimiento de cámara es medido y un destello breve en el borde enlaza las escenas; las animaciones del notch se graban en tiempo real a 30 fps.

El simulador usa datos de ejemplo; no es una grabación en directo. La galería y los ajustes de pantalla son capturas WPF reales del código actual con datos de demostración. `tour-ui-widgets-{es,en}.png` y `tour-ui-display-{es,en}.png` son las imágenes fuente.

La pista de `../audio/tour-soundtrack.m4a` se comparte entre idiomas y se copia a los videos sin volver a comprimir el audio. El SHA-256 figura arriba.

Instala Python con Pillow e imageio-ffmpeg, Node.js, Chrome y .NET 10. Puppeteer ya está disponible en `obj/tour-tooling` en este workspace. En un checkout limpio, el primer comando compila la app actual y regenera las capturas; después se revisan los fotogramas, se exportan los dos idiomas y se verifica la duración, el tamaño y la pista de audio.

Revisa fotogramas con `--stills` antes de codificar o sustituir el recorrido público. Las revisiones quedan en `obj/tour-work/review/`; los MP4 y pósteres solo se reemplazan al terminar correctamente. `--lang es|en` exporta un idioma y los parámetros `--chrome`, `--node` y `--dotnet` permiten elegir otras rutas.

La web usa `../video/isTargetSleeping-tour-{es,en}.mp4` y `../images/tour-{es,en}.jpg`. Los README enlazan el recorrido publicado. Conserva los MP4 de `docs/video/` como fuente compartida por la web y las descargas.
