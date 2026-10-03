namespace IsTargetSleeping;

public enum PetFeet { Stand, StepLeft, StepRight }
public enum PetProp { None, Laptop, Box, Cube, Crumb, Saddle }
public enum PetTint { Normal, Sleepy, Warm, Danger }

/// Ángulos de los bracitos, en grados desde la horizontal hacia fuera (positivo = abajo).
public static class PetArm
{
    public const double Out = 0;      // la pose del logo: las marcas de la cruz
    public const double Down = 50;
    public const double Up = -55;
    public const double Type = 78;    // hacia el teclado
    public const double Rub = -150;   // doblado hacia el ojo
    public const double Wave = -70;
}

/// Un instante de la mascota como números continuos, sin dibujo: cada especie lo
/// interpreta a su manera (Mira, en `UI/Pets/MiraPet.cs`). Medidas en píxeles de su
/// rejilla, con decimales: se dibuja a resolución real y nada va a saltos.
/// - `Eye`: 0 cerrado (párpado con pestañas) … 1 abierto (la diana); el párpado baja poco a poco.
/// - `LookX`/`LookY`: hacia dónde mira la pupila (−1 … 1).
/// - `Squash`: 1 aplastada, −1 estirada. `Bob`: el cuerpo sube (−) o baja (+). `Lean`: se inclina.
/// - `Sit`: 0 de pie … 1 tumbada (sin patas, más abajo). `Mouth`: boca abierta (0 … 1).
/// - `ArmL`/`ArmR`: ángulo de cada bracito (`PetArm`); `OneArm`: la derecha no se ve.
/// - `Prop` y `PropIn`: el accesorio y cuánto ha entrado (0 fuera … 1 en su sitio).
/// - `Beam`: el haz de escáner del ojo de Mira (0 … 1); apunta hacia donde mira la pupila.
/// - `BedIn`: la cama (0 … 1). `Blush`: sonrojo.
/// - `Antenna`: lo que se queda atrás la antena (muelle). `Behind`/`Peek`: escondida tras
///   el logo de Windows y cuánto se asoma por su izquierda.
/// - `EarL`/`EarR`/`Tail`: orejas y cola (−1 … 1; 0 neutro). `Stretch`: extensión del cuello/cuerpo.
/// - `Jaw`: la mandíbula de lado al rumiar (−1 … 1). `Arch`: lomo arqueado y pelo erizado (0 … 1).
/// - `Curl`: enroscada al dormir (0 … 1). `Pupil`: de rendija (0) a redonda y dilatada (1).
/// - `Bath`: metida en el baño termal (0 … 1). `Bird`: el pajarito, de lejos (0) a posado (1).
/// - `Scan`: el anillo de radar, que crece de 0 a 1 y se apaga (`Ring`): de 1 a 0 se salta sin verse.
/// - `PeekY`: cuánto se asoma por encima del logo (píxeles de rejilla; negativo, más abajo).
/// - `Lock`: la retícula de «objetivo fijado» (0 … 1).
/// - `Shake`: se sacude el pelo o la lana de lado (−1 … 1). `Wet`: recién salida del agua (0 … 1).
/// - `Croc`: el cocodrilo de la capibara, de fuera por la derecha (0) a su sitio (1); `CrocStep`: su
///   paso (−1 … 1). `Ride`: la capibara del suelo (0) a montada en su lomo (1).
/// - Frotarse el trasero por el suelo (sentada): `Drag`, el cuerpo se echa hacia delante sobre la
///   pata con el trasero quieto (0 … 1); `Rump`, el trasero se arrastra hacia delante (−1) o se queda
///   atrás (1); `Skid`, la marca de polvo que deja en el suelo, de recién hecha (0) a quedarse atrás
///   y borrarse (1).
/// Cada especie ignora los campos que no usa.
public readonly record struct PetPose(
    double Eye = 1, double LookX = 0, double LookY = 0,
    double Squash = 0, double Bob = 0, double Lean = 0, double Sit = 0,
    double ArmL = PetArm.Out, double ArmR = PetArm.Out, bool OneArm = false,
    PetFeet Feet = PetFeet.Stand, double Mouth = 0, double Blush = 0,
    PetProp Prop = PetProp.None, double PropIn = 1, double Beam = 0, double BedIn = 0,
    PetTint Tint = PetTint.Normal, double Antenna = 0, bool Behind = false, double Peek = 0,
    double EarL = 0, double EarR = 0, double Tail = 0, double Stretch = 0,
    double Jaw = 0, double Arch = 0, double Curl = 0, double Pupil = 0, double Bath = 0, double Bird = 0,
    double Scan = 0, double PeekY = 0, double Lock = 0, double Shake = 0, double Wet = 0,
    double Croc = 0, double CrocStep = 0, double Ride = 0, double Drag = 0, double Rump = 0, double Skid = 0)
{
    /// `new()` de un record struct no usa los valores por defecto: esta es la de reposo.
    public PetPose() : this(Eye: 1) { }

    /// Mezcla dos poses: los números se interpolan; lo discreto cambia a mitad. Un
    /// accesorio que aparece o desaparece entra o sale con `PropIn` en vez de saltar.
    public static PetPose Lerp(PetPose a, PetPose b, double k)
    {
        if (k <= 0) return a;
        if (k >= 1) return b;
        double L(double x, double y) => x + (y - x) * k;
        double aIn = a.Prop == PetProp.None ? 0 : a.PropIn, bIn = b.Prop == PetProp.None ? 0 : b.PropIn;
        var prop = a.Prop == b.Prop ? a.Prop : a.Prop == PetProp.None ? b.Prop : b.Prop == PetProp.None ? a.Prop : k < 0.5 ? a.Prop : b.Prop;
        bool half = k >= 0.5;
        return new(
            L(a.Eye, b.Eye), L(a.LookX, b.LookX), L(a.LookY, b.LookY),
            L(a.Squash, b.Squash), L(a.Bob, b.Bob), L(a.Lean, b.Lean), L(a.Sit, b.Sit),
            L(a.ArmL, b.ArmL), L(a.ArmR, b.ArmR), half ? b.OneArm : a.OneArm,
            half ? b.Feet : a.Feet, L(a.Mouth, b.Mouth), L(a.Blush, b.Blush),
            prop, a.Prop != b.Prop && a.Prop != PetProp.None && b.Prop != PetProp.None ? Math.Abs(1 - 2 * k) : L(aIn, bIn),
            L(a.Beam, b.Beam), L(a.BedIn, b.BedIn),
            half ? b.Tint : a.Tint, L(a.Antenna, b.Antenna), a.Behind || b.Behind, L(a.Peek, b.Peek),
            L(a.EarL, b.EarL), L(a.EarR, b.EarR), L(a.Tail, b.Tail), L(a.Stretch, b.Stretch),
            L(a.Jaw, b.Jaw), L(a.Arch, b.Arch), L(a.Curl, b.Curl), L(a.Pupil, b.Pupil), L(a.Bath, b.Bath), L(a.Bird, b.Bird),
            L(a.Scan, b.Scan), L(a.PeekY, b.PeekY), L(a.Lock, b.Lock), L(a.Shake, b.Shake), L(a.Wet, b.Wet),
            L(a.Croc, b.Croc), L(a.CrocStep, b.CrocStep), L(a.Ride, b.Ride), L(a.Drag, b.Drag), L(a.Rump, b.Rump), L(a.Skid, b.Skid));
    }

    /// Cuánto se ve la marca de polvo: nada al hacerse (0) ni al borrarse (1).
    public static double SkidAlpha(double skid) => skid is <= 0 or >= 1 ? 0 : Math.Sin(Math.PI * skid);

    /// Cuánto se ve el anillo de radar: aparece enseguida, se apaga al crecer y en 0 y 1 no está.
    public static double Ring(double scan) => scan is <= 0 or >= 1 ? 0 : Math.Min(1, scan * 6) * (1 - scan);

    /// La mayor diferencia entre dos poses, pesada por lo que se nota en pantalla (para
    /// comprobar que no hay saltos): el párpado entero son 7 píxeles de rejilla; un píxel
    /// de `Bob` o de `Squash`, menos de uno.
    public static double Distance(PetPose a, PetPose b) => new[]
    {
        Math.Abs(a.Eye - b.Eye), Math.Abs(a.LookX - b.LookX) * 0.5, Math.Abs(a.LookY - b.LookY) * 0.5,
        Math.Abs(a.Squash - b.Squash) * 0.4, Math.Abs(a.Bob - b.Bob) * 0.3, Math.Abs(a.Lean - b.Lean) * 0.3,
        Math.Abs(a.Sit - b.Sit) * 0.5, Math.Abs(a.ArmL - b.ArmL) / 90, Math.Abs(a.ArmR - b.ArmR) / 90,
        Math.Abs(a.Mouth - b.Mouth) * 0.6, Math.Abs(a.Blush - b.Blush) * 0.5, Math.Abs(a.Beam - b.Beam) * 0.5,
        Math.Abs(a.BedIn - b.BedIn) * 0.5, Math.Abs(a.Peek - b.Peek) / 3,
        Math.Abs(a.EarL - b.EarL) * 0.5, Math.Abs(a.EarR - b.EarR) * 0.5,
        Math.Abs(a.Tail - b.Tail) * 0.5, Math.Abs(a.Stretch - b.Stretch) * 0.5,
        Math.Abs(a.Jaw - b.Jaw) * 0.4, Math.Abs(a.Arch - b.Arch) * 0.5, Math.Abs(a.Curl - b.Curl) * 0.5,
        Math.Abs(a.Pupil - b.Pupil) * 0.4, Math.Abs(a.Bath - b.Bath) * 0.5, Math.Abs(a.Bird - b.Bird) * 0.5,
        // El anillo: lo que cambia su brillo y, mientras se ve, su tamaño.
        Math.Abs(Ring(a.Scan) - Ring(b.Scan)) * 0.6 + Math.Abs(a.Scan - b.Scan) * 0.4 * Math.Max(Ring(a.Scan), Ring(b.Scan)),
        Math.Abs(a.PeekY - b.PeekY) / 3, Math.Abs(a.Lock - b.Lock) * 0.5,
        Math.Abs(a.Shake - b.Shake) * 0.3, Math.Abs(a.Wet - b.Wet) * 0.5,
        // El cocodrilo recorre unos 34 píxeles de rejilla al entrar; la capibara sube unos 5 al montarse.
        Math.Abs(a.Croc - b.Croc) * 3, Math.Abs(a.CrocStep - b.CrocStep) * 0.3, Math.Abs(a.Ride - b.Ride) * 1.2,
        // Al frotarse, la cabeza se adelanta unos 2,9 píxeles y el trasero, 1,5; la marca se nota por su brillo.
        Math.Abs(a.Drag - b.Drag) * 1.0, Math.Abs(a.Rump - b.Rump) * 0.5,
        Math.Abs(SkidAlpha(a.Skid) - SkidAlpha(b.Skid)) * 0.5 + Math.Abs(a.Skid - b.Skid) * 0.2 * Math.Max(SkidAlpha(a.Skid), SkidAlpha(b.Skid)),
    }.Max();
}
