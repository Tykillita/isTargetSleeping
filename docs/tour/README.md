# Presentation sources / Fuentes de la presentación

`index.html` contains the original eleven-scene storyboard, bilingual script,
timings, easing, transitions and animations. `render.cjs` samples it deterministically
at 1920 × 1080, 30 fps, for exactly 77 seconds (2310 frames).

`../audio/tour-soundtrack.m4a` is the original soundtrack, shared by both languages.
The renderer copies the AAC stream without re-encoding, preserving music, effects,
timing and fades. SHA-256 of the source file:
`ca75fa81af140e7a531eceb8a1859ab19690b89e3edf8d64782ad742518eae89`.

`reference-sprites.json` retains the original cell geometry and frame counts.
The generator fills those cells with current procedural pet frames from the app's
`--export-pet`, including each species' real animation profile. Native screenshots
come from `../MediaCapture`, which references current app sources and renders WPF
at 384 DPI (4×). It uses demo data without starting engines or changing preferences.

Install Python with Pillow, numpy and imageio-ffmpeg, Node.js, Chrome, and the .NET 10 SDK:

```powershell
python -m pip install --target obj/tour-tooling Pillow numpy imageio-ffmpeg==0.6.0
npm install --prefix obj/tour-tooling puppeteer-core@25.12.0
python docs/generate-tours.py --capture
```

Review scene stills before replacing videos:

```powershell
python docs/generate-tours.py --stills 4.5,10,18.5,25,38,44.5,52,58,65,70.5,75
python docs/tour/verify.py
```

Review images live in `obj/tour-work/review/`. Final videos and posters replace the
tracked files only after a successful encode. `--chrome`, `--node` and `--dotnet`
accept custom runtime paths. `--lang es|en` renders one language.

---

`index.html` conserva las once escenas originales, el guion en ambos idiomas,
los tiempos, las curvas de movimiento, las transiciones y las animaciones.
`render.cjs` exporta 1920 × 1080 a 30 fps: 77 segundos y 2310 fotogramas exactos.

La pista original `../audio/tour-soundtrack.m4a` se copia sin volver a comprimirla:
se conservan música, efectos, sincronización y fundidos. El SHA-256 figura arriba.
`reference-sprites.json` conserva las celdas y cantidades de fotogramas del montaje;
sus imágenes se regeneran con los perfiles y dibujos actuales de `--export-pet`.

`../MediaCapture` usa las vistas WPF de la app actual a 384 DPI (4×), con datos de
ejemplo. No enciende motores ni modifica preferencias. Los comandos anteriores
instalan las dependencias, regeneran los medios y permiten revisar fotogramas.
Las revisiones quedan en `obj/tour-work/review/`; los videos y portadas solo se
sustituyen tras una exportación correcta. Los parámetros de rutas y de idioma
permiten usar otros ejecutables o exportar un solo idioma.

`verify.py` comprueba resolución, duración, cantidad de fotogramas, resolución de
las capturas y coincidencia exacta del audio en ambos videos.
