# Tour source / Fuentes del recorrido

`index.html` is an eight-scene, 77-second storyboard in English and Spanish. It embeds the website notch simulator and its animated states for the mascot, compact pill, music, lyrics and system views, modeled on the notch's behavior in the app. The Ollama state stays hidden. The desktop scene uses the current `AppWindow` Settings › General page, with bilingual labels and demo data.

The simulator uses its own sample data; it is not a live recording of the app. The Settings screen is a real WPF capture from the current source and uses demo data.

The soundtrack in `../audio/tour-soundtrack.m4a` is shared by both languages and copied into the videos without audio re-encoding. SHA-256: `ca75fa81af140e7a531eceb8a1859ab19690b89e3edf8d64782ad742518eae89`.

Install Python with Pillow and imageio-ffmpeg, Node.js, Chrome and the .NET 10 SDK. Puppeteer is already available through `obj/tour-tooling` in this workspace; to prepare it in a clean checkout:

```powershell
python -m pip install --target obj/tour-tooling Pillow imageio-ffmpeg==0.6.0
npm install --prefix obj/tour-tooling puppeteer-core@25.12.0
python docs/generate-tours.py --capture
```

Review scenes before encoding or replacing the public tour:

```powershell
python docs/generate-tours.py --stills 3,8,16,23,34,46,56,66,73,75
python docs/tour/verify.py
```

Review frames are saved in `obj/tour-work/review/`. Final MP4s and posters replace the tracked files only after a successful encode. `--lang es|en` renders one language; `--chrome`, `--node` and `--dotnet` accept custom runtime paths.

The site reads `../video/isTargetSleeping-tour-{es,en}.mp4` and `../images/tour-{es,en}.jpg`. README pages link to the deployed tour. Keep the MP4 in `docs/video/` as the shared source for the website and download.

---

`index.html` presenta un guion de ocho escenas y 77 segundos, en español e inglés. Inserta el simulador del notch de la web y sus estados animados de mascota, pastilla compacta, música, letras y sistema, inspirados en el comportamiento del notch de la app. La vista de Ollama permanece oculta. La escena de escritorio usa la página actual de `AppWindow` en Ajustes › General, con etiquetas en ambos idiomas y datos de demostración.

El simulador usa sus propios datos de ejemplo; no es una grabación en directo de la app. La pantalla de Ajustes es una captura WPF real del código actual con datos de demostración.

La pista de `../audio/tour-soundtrack.m4a` se comparte entre idiomas y se copia a los videos sin volver a comprimir el audio. El SHA-256 figura arriba.

Instala Python con Pillow e imageio-ffmpeg, Node.js, Chrome y .NET 10. Puppeteer ya está disponible en `obj/tour-tooling` en este workspace. En un checkout limpio, los comandos anteriores instalan las dependencias, compilan la app actual, regeneran capturas y portadas y preparan los videos.

Revisa fotogramas con `--stills` antes de codificar o sustituir el recorrido público. Las revisiones quedan en `obj/tour-work/review/`; los MP4 y pósteres solo se reemplazan al terminar correctamente. `--lang es|en` exporta un idioma y los parámetros `--chrome`, `--node` y `--dotnet` permiten elegir otras rutas.

La web usa `../video/isTargetSleeping-tour-{es,en}.mp4` y `../images/tour-{es,en}.jpg`. Los README enlazan el recorrido publicado. Conserva los MP4 de `docs/video/` como fuente compartida por la web y las descargas.
