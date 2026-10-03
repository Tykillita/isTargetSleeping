# Provisional cover / Portada provisional

The 1280 × 640 PNG is intended for GitHub Social preview and website sharing. The
512 × 512 PNG is a transparent white version of the app's native sleeping target.
Both come from `src/IsTargetSleeping/UI/Logo.cs`; the app icon remains unchanged.
The editable SVG cover is included alongside the PNG.

El PNG de 1280 × 640 es para Social preview de GitHub y enlaces de la web. El PNG
de 512 × 512 es una versión blanca transparente del símbolo nativo de la app.
Ambos usan `src/IsTargetSleeping/UI/Logo.cs`; el icono de la app se conserva.
La portada SVG editable acompaña al PNG.

From the repository root, on Windows with .NET 10 / Desde la raíz, en Windows:

```powershell
dotnet run --project docs/BrandArt -c Release -- .
```

Outputs / Salidas:

- `docs/images/social-preview.png`
- `docs/images/social-preview.svg`
- `Assets/istargetsleeping-mark-white-512.png`

Upload the cover in GitHub repository Settings → General → Social preview.
Sube la portada desde Ajustes del repositorio → General → Social preview.
