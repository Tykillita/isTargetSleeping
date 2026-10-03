using IsTargetSleeping;
using IsTargetSleeping.UI.Pets;

internal static class PetTests
{
    public static void Run(Action<string, bool> check)
    {
        check("catálogo: cuatro IDs permanentes en orden", PetCatalog.All.Select(p => p.Id).SequenceEqual(new[] { "mira", "llama", "capybara", "orange-cat" }));
        check("ajustes antiguos o especie desconocida: Mira", PetCatalog.Find(null).Id == "mira" && PetCatalog.Find("unknown").Id == "mira");
        var mixed = PetPose.Lerp(new(EarL: -1, EarR: 1, Tail: -1, Stretch: 0), new(EarL: 1, EarR: -1, Tail: 1, Stretch: 1), 0.5);
        check("orejas, cola y cuello se interpolan", mixed is { EarL: 0, EarR: 0, Tail: 0, Stretch: 0.5 }
            && PetPose.Distance(new(), new(Tail: 1)) > 0);
        var portraits = new List<uint[]>();
        var beds = new List<uint[]>();
        foreach (var pet in PetCatalog.All)
        {
            string id = pet.Id;
            var profile = pet.Animations;
            // Las reacciones de pie, en la cama y al final de una limpieza.
            var contexts = new[] { PetActivity.Alert, PetActivity.DeepSleep, PetActivity.Sweeping };
            var reactions = Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None).SelectMany(r => contexts.Select(a => (R: r, A: a))).ToArray();
            var allClips = Enum.GetValues<PetActivity>().Select(profile.For)
                .Concat(reactions.Select(x => profile.For(x.R, x.A)))
                .Concat(Enum.GetValues<PetGesture>().Where(g => g != PetGesture.None).Select(profile.Gesture)).Append(profile.Peek).ToArray();
            check($"{id}: clips reutilizados y tiempos finitos", allClips.All(c => c.Length > 0 && double.IsFinite(c.Length)) && ReferenceEquals(profile.For(PetActivity.Alert), profile.For(PetActivity.Alert)));
            check($"{id}: continuidad a 60 fps", allClips.All(c =>
                Enumerable.Range(0, (int)Math.Ceiling(c.Length * 60) + 1).All(i => PetPose.Distance(c.At(i / 60.0), c.At((i + 1) / 60.0)) < 0.36)));
            check($"{id}: reacciones duran hasta su última clave", reactions
                .All(x => !profile.For(x.R, x.A).Loop && Math.Abs(profile.ReactionDuration(x.R, x.A) - profile.For(x.R, x.A).Length) < 1e-9));
            check($"{id}: bostezo termina en su postura de dormir", profile.For(PetActivity.Yawning).At(100) == profile.Still(PetActivity.DeepSleep));

            var bed = id switch { "llama" => PetBed.Llama, "capybara" => PetBed.Capybara, "orange-cat" => PetBed.OrangeCat, _ => PetBed.Mira };
            var layer = new PixelCanvas(pet.Size.Width * 4, pet.Size.Height * 4);
            foreach (bool front in new[] { false, true }) PetBeds.Draw(layer, 4, bed, 0, front);
            check($"{id}: cama ausente fuera del sueño", layer.Pixels.All(px => px == 0));
            foreach (bool front in new[] { false, true }) PetBeds.Draw(layer, 4, bed, 1, front);
            beds.Add(layer.Pixels);
            var roomy = new PixelCanvas((pet.Size.Width + 4) * 4, (pet.Size.Height + 4) * 4);
            foreach (bool front in new[] { false, true }) PetBeds.Draw(roomy, 4, bed, 1, front);
            check($"{id}: cama completa dentro del lienzo", roomy.Pixels.Where((px, i) => i % roomy.Width >= layer.Width || i / roomy.Width >= layer.Height).All(px => px == 0));
            layer.Clear();
            foreach (bool front in new[] { false, true }) PetBeds.Draw(layer, 4, bed, 0.5, front);
            check($"{id}: cama entra con transparencia", layer.Pixels.Any(px => px >> 24 is > 0 and < 255)
                && layer.ToBgra().Chunk(4).All(px => px[0] <= px[3] && px[1] <= px[3] && px[2] <= px[3]));
            var asleep = new PixelCanvas(pet.Size.Width * 4, pet.Size.Height * 4);
            pet.Draw(asleep, profile.Still(PetActivity.DeepSleep), 4);
            var bare = new PixelCanvas(asleep.Width, asleep.Height);
            pet.Draw(bare, profile.Still(PetActivity.DeepSleep) with { BedIn = 0 }, 4);
            check($"{id}: dormir usa su cama", !asleep.Pixels.SequenceEqual(bare.Pixels));

            foreach (var activity in Enum.GetValues<PetActivity>().Where(a => a != PetActivity.Hidden))
            {
                var director = new PetDirector(pet.Anchors, 42, profile);
                var still = director.Step(0, 1 / 60.0, new(activity, Still: true));
                var later = director.Step(10, 1 / 60.0, new(activity, Still: true));
                check($"{id}/{activity}: movimiento reducido fijo", still.Pose == later.Pose && still.Fps == 0 && later.Dx == 0 && later.Dy == 0 && director.Particles.Live.Count == 0);
            }
            var brain = new PetBrain(profile);
            var toggled = new PetDirector(pet.Anchors, 42, profile);
            toggled.Step(0, 1 / 60.0, new(PetActivity.Alert));
            toggled.Step(0.1, 1 / 60.0, new(PetActivity.Working));
            var stopped = toggled.Step(0.12, 1 / 60.0, new(PetActivity.Working, Still: true));
            var fixedPose = toggled.Step(0.15, 1 / 60.0, new(PetActivity.Working, Still: true));
            check($"{id}: activar movimiento reducido durante una transición la detiene", stopped.Pose == profile.Still(PetActivity.Working)
                && fixedPose.Pose == stopped.Pose && fixedPose.Fps == 0);
            brain.Update(new(Up: true, ModelLoaded: true), 0);
            brain.React(PetReaction.Jump, 1);
            double end = 1 + profile.ReactionDuration(PetReaction.Jump, PetActivity.Alert);
            brain.Expire(end - 0.001);
            bool lasts = brain.Reaction == PetReaction.Jump;
            brain.Expire(end + 0.001);
            check($"{id}: el cerebro usa la duración del perfil", lasts && brain.Reaction == PetReaction.None);
            brain.React(PetReaction.Dizzy, 10);
            brain.React(PetReaction.Hearts, 10.1);
            brain.SetAnimations(PetProfiles.OrangeCat);
            brain.Expire(100);
            check($"{id}: cambiar perfil limpia la cola y conserva la actividad", brain.Reaction == PetReaction.None && brain.Activity == PetActivity.Alert);

            var idle = new PetIdle(9, profile);
            var twin = new PetIdle(9, profile);
            var selected = new List<(PetGesture G, double At)>();
            bool repeatable = true;
            for (int i = 0; i < 30000; i++)
            {
                double t = i / 60.0;
                var before = idle.Current;
                idle.Update(t, true, false);
                twin.Update(t, true, false);
                repeatable &= idle.Current == twin.Current;
                if (idle.Current != PetGesture.None && idle.Current != before) selected.Add((idle.Current, t));
            }
            check($"{id}: gestos reproducibles, sin repetición ni cabeceo despierta", repeatable && selected.Count > 8
                && selected.All(g => g.G != PetGesture.Nod)
                && selected.Zip(selected.Skip(1)).All(p => p.First.G != p.Second.G && p.Second.At - p.First.At >= profile.MinWait));
            idle.Update(1000, false, false);
            check($"{id}: ratón o paseo suspenden los gestos", idle.Current == PetGesture.None);

            foreach (uint dpi in new uint[] { 96, 120, 144, 192 })
            foreach (var placement in Enum.GetValues<PetPlacement>())
            {
                var monitor = new Win32.RECT { Right = 1920, Bottom = 1080 };
                var bar = new Win32.RECT { Top = 1032, Right = 1920, Bottom = 1080 };
                var start = new Win32.RECT { Left = 700, Top = 1032, Right = 748, Bottom = 1080 };
                var spot = TaskbarPetLayout.Place(placement, bar, start, null, monitor, dpi, pet.Size);
                check($"{id}/{dpi}/{placement}: dentro de pantalla", spot.Scale >= 1 && spot.X >= 0 && spot.Y >= 0
                    && spot.X + spot.Width <= 1920 && spot.Y + spot.Height <= 1080);
            }
            foreach (int scale in new[] { 1, 2, 3, 4 })
            {
                var c = new PixelCanvas(pet.Size.Width * scale, pet.Size.Height * scale);
                foreach (var clip in allClips)
                for (int i = 0; i <= 6; i++)
                {
                    c.Clear();
                    pet.Draw(c, clip.At(clip.Length * i / 6), scale);
                    if (!c.Pixels.Any(px => px >> 24 > 0)) throw new Exception($"Dibujo vacío: {id}");
                }
                check($"{id}/{scale}x: dibuja todas las poses y alfa premultiplicado", c.ToBgra().Chunk(4).All(px => px[0] <= px[3] && px[1] <= px[3] && px[2] <= px[3]));
            }
            var portrait = new PixelCanvas(128, 120);
            pet.Draw(portrait, profile.Still(PetActivity.Alert), 4);
            portraits.Add(portrait.Pixels);
            if (id != "mira")
            {
                bool fits = true;
                foreach (var clip in allClips)
                for (int i = 0; i <= 12; i++)
                {
                    var pose = clip.At(clip.Length * i / 12);
                    // Mientras entra la cama (o la tina del baño), su deslizamiento bajo el borde es intencional.
                    if (pose.BedIn is > 0 and < 1 || pose.Bath is > 0 and < 1) continue;
                    roomy.Clear();
                    pet.Draw(roomy, pose, 4);
                    bool frameFits = roomy.Pixels.Where((px, at) => at % roomy.Width >= portrait.Width || at / roomy.Width >= portrait.Height).All(px => px == 0);
                    if (!frameFits && fits) Console.WriteLine($"Primer recorte {id}: {pose}");
                    fits &= frameFits;
                }
                check($"{id}: anatomía y accesorios sin recortes a lo largo de los clips", fits);
            }
            check($"{id}: paseo distingue izquierda y derecha", profile.Walk(2, -1, 0, false).LookX < 0 && profile.Walk(2, 1, 0, false).LookX > 0);
        }
        check("las cuatro siluetas son distintas", portraits.Select(p => string.Join(',', p)).Distinct().Count() == 4);
        check("las cuatro camas tienen diseños propios", beds.Select(p => string.Join(',', p)).Distinct().Count() == 4);
        var capybara = PetCatalog.Find("capybara");
        var capyRest = capybara.Animations.Still(PetActivity.Alert);
        var capyNormal = new PixelCanvas(128, 120);
        var capyArms = new PixelCanvas(128, 120);
        capybara.Draw(capyNormal, capyRest, 4);
        capybara.Draw(capyArms, capyRest with { ArmL = PetArm.Up, ArmR = PetArm.Rub, OneArm = true }, 4);
        check("capibara: las poses de brazos no dibujan extremidades extra", capyNormal.Pixels.SequenceEqual(capyArms.Pixels));
        var llama = PetCatalog.Find("llama");
        var llamaRest = llama.Animations.Still(PetActivity.Alert);
        var llamaNormal = new PixelCanvas(128, 120);
        var llamaArms = new PixelCanvas(128, 120);
        llama.Draw(llamaNormal, llamaRest, 4);
        llama.Draw(llamaArms, llamaRest with { ArmL = PetArm.Up, ArmR = PetArm.Rub, OneArm = true }, 4);
        check("llama: sin brazos (las poses de brazos no dibujan nada)", llamaNormal.Pixels.SequenceEqual(llamaArms.Pixels));
        var llamaClips = Enum.GetValues<PetActivity>().Select(llama.Animations.For)
            .Concat(Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None)
                .SelectMany(r => new[] { PetActivity.Alert, PetActivity.DeepSleep, PetActivity.Sweeping }.Select(a => llama.Animations.For(r, a))))
            .Concat(Enum.GetValues<PetGesture>().Select(llama.Animations.Gesture)).Append(llama.Animations.Peek)
            .SelectMany(c => Enumerable.Range(0, 41).Select(i => c.At(c.Length * i / 40))).ToList();
        check("llama: ningún clip usa brazos y mueve la cola", llamaClips.All(p => p.ArmL == PetArm.Out && p.ArmR == PetArm.Out && !p.OneArm)
            && llamaClips.Max(p => p.Tail) > 0.9 && llamaClips.Min(p => p.Tail) < -0.9);
        check("personalidades: velocidades y esperas acordadas", PetProfiles.Llama.WalkSpeed == 14 && PetProfiles.Llama.MinWait == 6 && PetProfiles.Llama.MaxWait == 12
            && PetProfiles.Capybara.WalkSpeed == 7 && PetProfiles.Capybara.MinWait == 12 && PetProfiles.Capybara.MaxWait == 22
            && PetProfiles.OrangeCat.WalkSpeed == 18 && PetProfiles.OrangeCat.MinWait == 4 && PetProfiles.OrangeCat.MaxWait == 9);
        Personalities(check);
    }

    /// Cada mascota se mueve, reacciona, trata al ratón y suelta partículas a su manera.
    private static void Personalities(Action<string, bool> check)
    {
        var profiles = PetCatalog.All.Select(p => (p.Id, P: p.Animations, p.Anchors)).ToList();

        // Distintas entre sí.
        var clicks = profiles.Select(p => p.P.For(PetReaction.Jump, PetActivity.Alert)).ToList();
        check("clic: cada una reacciona con su propio clip y duración",
            profiles.Select(p => Math.Round(p.P.ReactionDuration(PetReaction.Jump, PetActivity.Alert), 3)).Distinct().Count() == 4
            && clicks.Select(c => string.Join(';', c.Keys)).Distinct().Count() == 4);
        foreach (var (id, profile, _) in profiles)
        {
            var mine = profile.IdleGestures(false).Concat(profile.IdleGestures(true)).Select(g => g.Gesture).ToHashSet();
            var others = profiles.Where(o => o.Id != id)
                .SelectMany(o => o.P.IdleGestures(false).Concat(o.P.IdleGestures(true))).Select(g => g.Gesture).ToHashSet();
            check($"{id}: al menos dos gestos propios", mine.Count(g => !others.Contains(g)) >= 2);
        }
        check("ritmos y esperas distintos", profiles.Select(p => p.P.WalkSpeed).Distinct().Count() == 4
            && profiles.Select(p => (p.P.MinWait, p.P.MaxWait)).Distinct().Count() == 4
            && profiles.Select(p => p.P.LookSpeed).Distinct().Count() == 4);
        var capy = PetProfiles.Capybara;
        check("capibara: no se esconde tras el logo, se apoya en él", !capy.HidesBehindLogo
            && Enumerable.Range(0, 100).All(i => !capy.Peek.At(capy.Peek.Length * i / 100).Behind) && capy.PeekStill.Lean > 0);
        var llama = PetProfiles.Llama;
        var llamaPeek = Enumerable.Range(0, 160).Select(i => llama.Peek.At(llama.Peek.Length * i / 160)).ToList();
        check("llama: asoma por arriba del logo (PeekY), no por el lado", llamaPeek.All(p => p.Behind && p.Peek == 0)
            && llamaPeek.Max(p => p.PeekY) - llamaPeek.Min(p => p.PeekY) >= 4 && llamaPeek.Max(p => p.Stretch) > 0.9);
        var cat = PetProfiles.OrangeCat;
        var catPeek = Enumerable.Range(0, 160).Select(i => cat.Peek.At(cat.Peek.Length * i / 160)).ToList();
        int earsAt = catPeek.FindIndex(p => p.PeekY > -0.1), eyesAt = catPeek.FindIndex(p => p.Peek > 2);
        check("gato: asoman primero las orejas y luego los ojos", earsAt >= 0 && eyesAt > earsAt);
        check("solo el gato se lanza sobre el cursor", profiles.Count(p => p.P.PouncesOnCursor) == 1 && cat.PouncesOnCursor);

        // Sin saltos en ninguna transición, también al esconderse o salir de detrás del logo.
        foreach (var (id, profile, anchors) in profiles)
        {
            var rough = new List<string>();
            foreach (var from in Enum.GetValues<PetActivity>())
            foreach (var to in Enum.GetValues<PetActivity>())
            foreach (var (fromHiding, toHiding) in new[] { (false, false), (true, false), (false, true) })
                if (profile.Transition(from, to, fromHiding, toHiding) is { } clip
                    && Enumerable.Range(0, (int)Math.Ceiling(clip.Length * 60) + 1).Any(i => PetPose.Distance(clip.At(i / 60.0), clip.At((i + 1) / 60.0)) >= 0.36))
                    rough.Add($"{from}{(fromHiding ? "*" : "")}→{to}{(toHiding ? "*" : "")}");
            check($"{id}: transiciones sin saltos a 60 fps{(rough.Count > 0 ? ": " + string.Join(", ", rough.Take(5)) : "")}", rough.Count == 0);

            // El ratón entra, se mueve y se va: la pose cambia sin saltos.
            var director = new PetDirector(anchors, 5, profile);
            var poses = new List<PetPose>();
            for (double t = 0; t < 4; t += 1 / 60.0)
                poses.Add(director.Step(t, 1 / 60.0, new(PetActivity.Alert, Hovered: t is > 0.5 and < 3, HoverX: Math.Sin(t * 3), HoverY: -0.3)).Pose);
            check($"{id}: el ratón entra y sale sin saltos", poses.Zip(poses.Skip(1)).All(p => PetPose.Distance(p.First, p.Second) < 0.36));
        }

        // Cada una con su trato al ratón.
        PetPose Hovered(string id, double seconds)
        {
            var pet = PetCatalog.Find(id);
            var d = new PetDirector(pet.Anchors, 5, pet.Animations);
            PetPose last = new();
            for (double t = 0; t < seconds; t += 1 / 60.0) last = d.Step(t, 1 / 60.0, new(PetActivity.Alert, Hovered: true, HoverX: 1, HoverY: 0)).Pose;
            return last;
        }
        check("Mira fija la retícula sobre el cursor", Hovered("mira", 1).Lock > 0.95);
        check("la llama estira el cuello y se inclina hacia el cursor", Hovered("llama", 1) is { Stretch: >= 0.75, Lean: > 0.5 });
        check("el gato se agacha con las pupilas dilatadas", Hovered("orange-cat", 1) is { Pupil: > 0.95, Squash: > 0.3 });
        check("la capibara mira más despacio que el gato", Hovered("capybara", 0.25).LookX < Hovered("orange-cat", 0.25).LookX - 0.3
            && Hovered("orange-cat", 0.25).LookX > 0.8 && Hovered("capybara", 2).Eye <= 0.55);

        var pounce = new CursorPounce();
        bool early = false;
        for (double t = 0; t < 1.15; t += 1 / 60.0) early |= pounce.Update(t, true, 10, 10);
        bool fired = false;
        for (double t = 1.15; t < 1.3; t += 1 / 60.0) fired |= pounce.Update(t, true, 10, 10);
        bool again = false;
        for (double t = 1.3; t < 5.0; t += 1 / 60.0) again |= pounce.Update(t, true, 11, 10);
        bool afterCooldown = false;
        for (double t = 5.0; t < 6.5; t += 1 / 60.0) afterCooldown |= pounce.Update(t, true, 11, 10);
        check("pounce: se lanza tras 1,2 s quieto, con enfriamiento", !early && fired && !again && afterCooldown);
        var moving = new CursorPounce();
        bool chased = false;
        for (double t = 0; t < 5; t += 1 / 60.0) chased |= moving.Update(t, true, (int)(t * 60) % 40, 10);
        var leaving = new CursorPounce();
        bool left = false;
        for (double t = 0; t < 5; t += 1 / 60.0) left |= leaving.Update(t, t % 1 < 0.9, 10, 10);
        check("pounce: no si el cursor se mueve ni si se va antes de tiempo", !chased && !left);

        // Partículas propias.
        HashSet<ParticleKind> Drops(string id, PetActivity activity, double seconds)
        {
            var pet = PetCatalog.Find(id);
            var d = new PetDirector(pet.Anchors, 5, pet.Animations);
            var kinds = new HashSet<ParticleKind>();
            for (double t = 0; t < seconds; t += 1 / 60.0)
            {
                d.Step(t, 1 / 60.0, new(activity));
                kinds.UnionWith(d.Particles.Live.Select(p => p.Kind));
            }
            return kinds;
        }
        var expected = new Dictionary<string, (ParticleKind Sleep, ParticleKind Work)>
        {
            ["mira"] = (ParticleKind.Z, ParticleKind.Bit),
            ["llama"] = (ParticleKind.Note, ParticleKind.Note),
            ["capybara"] = (ParticleKind.Steam, ParticleKind.Leaf),
            ["orange-cat"] = (ParticleKind.Z, ParticleKind.Purr),
        };
        var signatures = new List<string>();
        foreach (var (id, (sleep, work)) in expected)
        {
            var asleep = Drops(id, PetActivity.DeepSleep, 8);
            var working = Drops(id, PetActivity.Working, 6);
            check($"{id}: suelta lo suyo dormida ({sleep}) y trabajando ({work})", asleep.Contains(sleep) && working.Contains(work));
            signatures.Add(string.Join(',', asleep.Order()) + "|" + string.Join(',', working.Order()));
        }
        check("capibara: vapor y burbujas en su baño", Drops("capybara", PetActivity.DeepSleep, 8).IsSupersetOf([ParticleKind.Steam, ParticleKind.Bubble]));
        check("dormidas y trabajando, cada una suelta partículas distintas", signatures.Distinct().Count() == 4);

        var llamaPet = PetCatalog.Find("llama");
        var snort = new PetDirector(llamaPet.Anchors, 5, llamaPet.Animations);
        snort.Step(0, 1 / 60.0, new(PetActivity.Alert));
        snort.Step(1 / 60.0, 1 / 60.0, new(PetActivity.Alert, PetReaction.Jump, 1 / 60.0));
        var (mx, my) = llamaPet.Anchors.Mouth;
        var puffs = snort.Particles.Live.Where(p => p.Kind == ParticleKind.Puff).ToList();
        check("el resoplido de la llama sale de la boca", puffs.Count > 0 && puffs.All(p => Math.Abs(p.X - mx) < 4 && Math.Abs(p.Y - my) < 2));

        var spot = new PetAnchors(14, 8, 14, 20, 14, 15, 29, 14, 19);
        var kinds = new[] { ParticleKind.Steam, ParticleKind.Note, ParticleKind.Bubble, ParticleKind.Bit, ParticleKind.Leaf, ParticleKind.Puff, ParticleKind.Purr, ParticleKind.Signal };
        var physics = new PetParticles(spot, 4);
        foreach (var kind in kinds) physics.Emit(kind);
        var startAt = physics.Live.ToArray();
        for (int i = 0; i < 30; i++) physics.Update(1 / 60.0);
        Particle Of(ParticleKind kind) => physics.Live.First(p => p.Kind == kind);
        Particle Start(ParticleKind kind) => startAt.First(p => p.Kind == kind);
        check("el vapor, las notas, las burbujas y los bits suben; las hojas caen",
            new[] { ParticleKind.Steam, ParticleKind.Note, ParticleKind.Bubble, ParticleKind.Bit }.All(k => Of(k).Y < Start(k).Y)
            && Of(ParticleKind.Leaf).Y > Start(ParticleKind.Leaf).Y);
        check("el resoplido se frena y crece; el vapor se hincha", Of(ParticleKind.Puff).Vx < Start(ParticleKind.Puff).Vx * 0.5
            && Of(ParticleKind.Puff).Scale > Start(ParticleKind.Puff).Scale && Of(ParticleKind.Steam).Scale > 1);
        check("la burbuja revienta al final", new Particle(ParticleKind.Bubble, 0, 0, 0, 0, 0.95, 1, 0).Scale > 1.5
            && new Particle(ParticleKind.Bubble, 0, 0, 0, 0, 0.99, 1, 0).Alpha < 0.2);
        string Run(int seed)
        {
            var fx = new PetParticles(spot, seed);
            for (double t = 0; t < 3; t += 1 / 60.0)
            {
                foreach (var kind in kinds) fx.Drip(kind, 0.4, t);
                fx.Update(1 / 60.0);
            }
            return string.Join(';', fx.Live);
        }
        check("partículas nuevas: misma semilla, mismo resultado", Run(8) == Run(8) && Run(8) != Run(9));

        // El anillo de radar: de 1 a 0 no se ve el salto; mientras crece, sí se ve.
        check("el anillo de radar se apaga al terminar", PetPose.Ring(0) == 0 && PetPose.Ring(1) == 0 && PetPose.Ring(0.3) > 0.5
            && PetPose.Distance(new(Scan: 1), new(Scan: 0)) == 0 && PetPose.Distance(new(Scan: 0.3), new(Scan: 0)) > 0.3);
        check("campos nuevos: se interpolan", PetPose.Lerp(new(), new(Jaw: 1, Arch: 1, Curl: 1, Pupil: 1, Bath: 1, Bird: 1, PeekY: 2, Lock: 1, Shake: 1, Wet: 1), 0.5)
            is { Jaw: 0.5, Arch: 0.5, Curl: 0.5, Pupil: 0.5, Bath: 0.5, Bird: 0.5, PeekY: 1, Lock: 0.5, Shake: 0.5, Wet: 0.5 }
            && PetPose.Distance(new(), new(Wet: 1)) > 0 && PetPose.Distance(new(), new(Shake: 1)) > 0);
        var drops = new PetParticles(spot, 4);
        drops.Emit(ParticleKind.Drop, 6);
        var flung = drops.Live.ToArray();
        for (int i = 0; i < 20; i++) drops.Update(1 / 60.0);
        check("las gotas salen despedidas a los lados y caen", flung.All(p => Math.Sign(p.Vx) == Math.Sign(p.X - spot.CenterX) && Math.Abs(p.Vx) >= 4)
            && drops.Live.Count == flung.Length && drops.Live.Zip(flung).All(p => p.First.Vy > p.Second.Vy));

        Cleaning(check);
        Crocodile(check);
        Waking(check);
    }

    /// Cada una limpia a su manera y desde donde esté: dormida sale de su cama (la capibara,
    /// de la tina) y el destello llega al final; dormida reacciona sin salir de la cama.
    private static void Cleaning(Action<string, bool> check)
    {
        IEnumerable<PetPose> Sample(PetClip clip, int steps = 60) => Enumerable.Range(0, steps + 1).Select(i => clip.At(clip.Length * i / steps));
        foreach (var pet in PetCatalog.All)
        {
            var profile = pet.Animations;
            var sweeping = Sample(profile.For(PetActivity.Sweeping))
                .Concat(Enum.GetValues<PetActivity>().SelectMany(from => profile.Transition(from, PetActivity.Sweeping, false, false) is { } t ? Sample(t) : []))
                .Append(profile.Still(PetActivity.Sweeping)).ToList();
            bool scans = sweeping.Any(p => p.Beam > 0.9) && sweeping.Any(p => p.Prop == PetProp.Cube && p.PropIn > 0.9);
            check($"{pet.Id}: limpia a su manera ({(pet.Id == "mira" ? "escanea y compacta" : "sin escáner")})",
                pet.Id == "mira" ? scans : sweeping.All(p => p.Beam == 0 && p.Prop != PetProp.Cube));

            if (profile.Transition(PetActivity.DeepSleep, PetActivity.Sweeping, false, false) is { } wake)
                check($"{pet.Id}: para limpiar sale de la cama", wake.At(0).BedIn > 0.9 && wake.At(wake.Length).BedIn < 0.05);
            else check($"{pet.Id}: para limpiar sale de la cama", false);

            // Dormida: ninguna reacción la saca de la cama ni la mueve de su sitio.
            var inBed = Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None)
                .All(r => Sample(profile.For(r, PetActivity.DeepSleep)).All(p => p.BedIn >= 0.8)
                    && Enumerable.Range(0, 40).All(i => profile.Motion(r, i * 0.05, PetActivity.DeepSleep) == (0, 0))
                    && profile.Still(r, PetActivity.DeepSleep).BedIn >= 0.8);
            check($"{pet.Id}: dormida reacciona sin salir de la cama", inBed);
            var director = new PetDirector(pet.Anchors, 5, profile);
            var poses = new List<PetFrame>();
            for (double t = 0; t < 3; t += 1 / 60.0)
                poses.Add(director.Step(t, 1 / 60.0, new(PetActivity.DeepSleep, PetReaction.Jump, 0.5, ReactionDuring: PetActivity.DeepSleep)));
            check($"{pet.Id}: un clic dormida no enciende el logo ni la levanta", poses.All(f => f.Glow.Dark && f.Pose.BedIn >= 0.8));
        }

        // La capibara: recién salida de la tina se escurre; despierta, se frota el trasero.
        var capy = PetProfiles.Capybara;
        var capyPet = PetCatalog.Find("capybara");
        var afterTub = new PetContext(PetActivity.Sweeping, PetActivity.DeepSleep);
        var fromTub = capy.Transition(PetActivity.DeepSleep, PetActivity.Sweeping, false, false)!;
        check("capibara: sale de la tina mojada", fromTub.At(fromTub.Length).Wet > 0.5 && fromTub.At(fromTub.Length) == capy.For(afterTub).At(0));
        var wring = Sample(capy.For(afterTub)).ToList();
        check("capibara: tras la tina se escurre sacudiéndose", wring.Min(p => p.Shake) < -0.9 && wring.Max(p => p.Shake) > 0.9 && wring.All(p => p.Wet > 0.5)
            && capy.For(new PetContext(PetActivity.Sweeping, PetActivity.Yawning)) == capy.For(afterTub));
        var sitDown = capy.Transition(PetActivity.Alert, PetActivity.Sweeping, false, false)!;
        var scoot = Sample(capy.For(PetActivity.Sweeping)).ToList();
        check("capibara: despierta no se mete en la tina, se sienta a frotarse",
            Sample(sitDown).All(p => p.Bath == 0 && p.Wet == 0) && sitDown.At(sitDown.Length).Sit >= 0.9
            && scoot.All(p => p is { Sit: >= 0.9, Bath: 0, Wet: 0, Shake: 0 }) && capy.For(new PetContext(PetActivity.Sweeping, PetActivity.Alert)) == capy.For(PetActivity.Sweeping));
        // Se frota sin moverse de su sitio: el cuerpo se echa adelante con el trasero quieto, el
        // trasero se arrastra y la marca de polvo se queda atrás; la mascota entera no se desplaza.
        var motions = Enumerable.Range(0, 48).Select(i => capy.Motion(new PetContext(PetActivity.Sweeping, PetActivity.Alert), i * 0.05)).ToList();
        check("capibara: se frota el trasero sin desplazar toda la mascota", motions.All(m => m == (0, 0))
            && scoot.Max(p => p.Drag) > 0.9 && scoot.Min(p => p.Rump) < -0.5 && scoot.Max(p => p.Rump) > 0.2
            && scoot.Max(p => PetPose.SkidAlpha(p.Skid)) > 0.5);
        var frames = new PetDirector(capyPet.Anchors, 5, capy);
        var played = new List<PetPose>();
        for (double t = 0; t < 4.8; t += 1 / 60.0) played.Add(frames.Step(t, 1 / 60.0, new(PetActivity.Sweeping, From: PetActivity.Alert)).Pose);
        check("capibara: la marca de polvo no parpadea al volver a empezar", played.Zip(played.Skip(1))
            .All(f => Math.Abs(PetPose.SkidAlpha(f.First.Skid) - PetPose.SkidAlpha(f.Second.Skid)) < 0.2));
        var wide = new PixelCanvas(capyPet.Size.Width * 3, capyPet.Size.Height * 3);
        capyPet.Draw(wide, capy.For(PetActivity.Sweeping).At(0.35), 3);
        check("capibara: echada hacia delante no se sale del lienzo", Enumerable.Range(0, wide.Height).All(y => wide.Pixels[y * wide.Width] == 0));
        HashSet<ParticleKind> Seen(PetActivity from)
        {
            var director = new PetDirector(capyPet.Anchors, 5, capy);
            var seen = new HashSet<ParticleKind>();
            for (double t = 0; t < 4; t += 1 / 60.0)
            {
                director.Step(t, 1 / 60.0, new(PetActivity.Sweeping, From: from));
                seen.UnionWith(director.Particles.Live.Select(p => p.Kind));
            }
            return seen;
        }
        var wetDrops = Seen(PetActivity.DeepSleep);
        var dust = Seen(PetActivity.Alert);
        check("capibara: gotas al escurrirse, polvo al frotarse", wetDrops.Contains(ParticleKind.Drop) && !wetDrops.Contains(ParticleKind.Dust)
            && dust.Contains(ParticleKind.Dust) && !dust.Contains(ParticleKind.Drop));
        var finale = capy.For(PetReaction.Sparkle, PetActivity.Sweeping);
        var wetFinale = capy.For(PetReaction.Sparkle, afterTub);
        check("capibara: cada limpieza con su final, seca y brillante (despierta, sin levantarse)", finale != wetFinale
            && finale != capy.For(PetReaction.Sparkle, PetActivity.Alert)
            && finale.At(0).Sit >= 0.9 && finale.At(finale.Length) is { Sit: >= 0.7, Wet: 0 } && Sample(finale).All(p => p.Sit >= 0.7)
            && wetFinale.At(0).Wet > 0.5 && wetFinale.At(wetFinale.Length).Wet == 0
            && capy.Drips(PetReaction.Sparkle, PetActivity.Sweeping).Any(d => d.Kind == ParticleKind.Sparkle)
            && capy.Drips(PetReaction.Sparkle, afterTub).Any(d => d.Kind == ParticleKind.Sparkle)
            && capy.Burst(PetReaction.Sparkle, PetActivity.Sweeping).Any(b => b.Kind == ParticleKind.Dust)
            && capy.Burst(PetReaction.Sparkle, afterTub).Any(b => b.Kind == ParticleKind.Drop));
        check("capibara: en movimiento reducido, cada limpieza con su pose", capy.Still(afterTub).Wet > 0.5 && capy.Still(PetActivity.Sweeping) is { Sit: >= 0.9, Wet: 0 });
        var wash = Sample(PetProfiles.OrangeCat.For(PetActivity.Sweeping)).ToList();
        check("gato: se lava a lametones", wash.Any(p => p.ArmR <= PetArm.Rub + 1 && p.Mouth > 0.2));
        var wool = Sample(PetProfiles.Llama.For(PetActivity.Sweeping)).ToList();
        check("llama: se sacude la lana", wool.Min(p => p.Shake) < -0.9 && wool.Max(p => p.Shake) > 0.9);
        var mira = PetProfiles.Mira;
        var scan = Sample(mira.For(PetActivity.Sweeping)).ToList();
        check("Mira: el haz recorre la barra de lado a lado", scan.Where(p => p.Beam > 0.9).Min(p => p.LookX) < -0.9
            && scan.Where(p => p.Beam > 0.9).Max(p => p.LookX) > 0.9);
        var miraPet = PetCatalog.Find("mira");
        var vacuum = new PetDirector(miraPet.Anchors, 5, mira);
        var fragments = new List<Particle>();
        for (double t = 0; t < 3; t += 1 / 60.0)
        {
            vacuum.Step(t, 1 / 60.0, new(PetActivity.Sweeping));
            fragments.AddRange(vacuum.Particles.Live.Where(p => p.Kind == ParticleKind.Fragment));
        }
        double Gap(Particle p) => Math.Abs(p.X - miraPet.Anchors.EyeX) + Math.Abs(p.Y - miraPet.Anchors.EyeY);
        check("Mira: los fragmentos de datos vuelan hacia su ojo", fragments.Count > 0
            && fragments.Where(p => p.Age < 0.05).Average(Gap) > 6 && fragments.Where(p => p.Age > p.Life * 0.8).Average(Gap) < 2);
        var compact = mira.For(PetReaction.Sparkle, PetActivity.Sweeping);
        check("Mira: el final compacta el cubo hasta que desaparece", compact.At(0) is { Prop: PetProp.Cube, PropIn: > 0.5 }
            && Sample(compact).Any(p => p.Prop == PetProp.Cube && p.PropIn < 0.01) && compact.At(compact.Length).Prop == PetProp.None
            && Sample(compact).Max(p => PetPose.Ring(p.Scan)) > 0.5);

        // El cerebro: la limpieza se ve entera y el destello llega al final, donde limpia.
        var brain = new PetBrain(capy);
        brain.Update(new(), 0);
        brain.Update(new(Cleaning: true), 10);
        check("cerebro: limpiar dormida pasa a limpiar", brain.Activity == PetActivity.Sweeping && brain.ActivitySince == 10);
        check("cerebro: recuerda de dónde venía", brain.ActivityFrom == PetActivity.DeepSleep);
        double shown = capy.Transition(PetActivity.DeepSleep, PetActivity.Sweeping, false, false)!.Length + capy.For(afterTub).Length;
        brain.Update(new(), 10.5);
        brain.React(PetReaction.Sparkle, 10.5, finale: true);
        check("cerebro: una limpieza corta se sigue viendo", brain.Activity == PetActivity.Sweeping && brain.Reaction == PetReaction.None);
        brain.Expire(10 + shown - 0.01);
        bool waited = brain.Reaction == PetReaction.None && brain.Activity == PetActivity.Sweeping;
        brain.Expire(10 + shown + 0.01);
        check("cerebro: el destello llega cuando se ha visto limpiar, donde limpia", waited && brain.Reaction == PetReaction.Sparkle
            && brain.ReactionDuring == PetActivity.Sweeping && brain.ReactionFrom == PetActivity.DeepSleep && brain.Activity == PetActivity.Sweeping);
        double end = brain.ReactionSince + capy.ReactionDuration(PetReaction.Sparkle, afterTub);
        brain.Expire(end - 0.01);
        bool during = brain.Activity == PetActivity.Sweeping;
        brain.Expire(end + 0.01);
        check("cerebro: tras el destello vuelve a dormir", during && brain.Activity == PetActivity.DeepSleep && brain.Reaction == PetReaction.None);
        brain.React(PetReaction.Jump, 100);
        check("cerebro: dormida, la reacción es de la cama", brain.ReactionDuring == PetActivity.DeepSleep);

        var quiet = new PetBrain(capy);
        quiet.Update(new(Up: true, ModelLoaded: true), 0);
        quiet.Update(new(Up: true, ModelLoaded: true, Cleaning: true), 1);
        quiet.Update(new(Up: true, ModelLoaded: true), 1.2);
        double minimum = Math.Min(PetBrain.CleanHoldMax, (capy.Transition(PetActivity.Alert, PetActivity.Sweeping, false, false)?.Length ?? 0) + capy.For(PetActivity.Sweeping).Length);
        check("cerebro: el mínimo de la limpieza despierta es el de frotarse", Math.Abs(capy.HoldFor(PetActivity.Alert, PetActivity.Sweeping) - minimum) < 1e-9
            && capy.HoldFor(PetActivity.DeepSleep, PetActivity.Sweeping) == Math.Min(PetBrain.CleanHoldMax, shown));
        quiet.Expire(1 + minimum - 0.01);
        bool still = quiet.Activity == PetActivity.Sweeping;
        quiet.Expire(1 + minimum + 0.01);
        check("cerebro: sin nada liberado, vuelve al cumplir el mínimo", still && quiet.Activity == PetActivity.Alert && quiet.Reaction == PetReaction.None);
        quiet.Update(new(Up: true, ModelLoaded: true, Cleaning: true), 20);
        quiet.Update(new(Up: true, Game: true), 20.2);
        check("cerebro: un juego la oculta aunque esté limpiando", quiet.Activity == PetActivity.Hidden);

        // El resultado puede llegar después de cumplir el mínimo (la medición espera
        // cinco segundos): OnState reserva el final antes de quitar Cleaning.
        foreach (var pet in PetCatalog.All)
        foreach (var origin in new[] { PetActivity.DeepSleep, PetActivity.Alert })
        {
            bool up = origin == PetActivity.Alert;
            var late = new PetBrain(pet.Animations);
            late.Update(new(Up: up, ModelLoaded: up), 0);
            late.Update(new(Up: up, ModelLoaded: up, Cleaning: true), 10);
            late.Expire(20); // más tarde que cualquier mínimo de limpieza
            late.React(PetReaction.Sparkle, 20, finale: true);
            late.Update(new(Up: up, ModelLoaded: up), 20);
            bool kept = late.Activity == PetActivity.Sweeping && late.Reaction == PetReaction.Sparkle
                && late.ReactionDuring == PetActivity.Sweeping && late.ReactionFrom == origin;
            double finished = 20 + pet.Animations.ReactionDuration(PetReaction.Sparkle, new PetContext(PetActivity.Sweeping, origin));
            late.Expire(finished - 0.01);
            kept &= late.Activity == PetActivity.Sweeping;
            late.Expire(finished + 0.01);
            check($"{pet.Id}: el final tardío conserva el contexto desde {origin} y termina",
                kept && late.Activity == origin && late.Reaction == PetReaction.None);

            late.Update(new(Up: up, ModelLoaded: up, Cleaning: true), 30);
            late.Update(new(Up: up, ModelLoaded: up), 40);
            check($"{pet.Id}: sin cambio positivo, una limpieza larga sale sin destello desde {origin}",
                late.Activity == origin && late.Reaction == PetReaction.None);

            late.Update(new(Up: up, ModelLoaded: up, Cleaning: true), 50);
            late.React(PetReaction.Sparkle, 60, finale: true);
            late.Update(new(Up: up, ModelLoaded: up, Game: true), 60);
            check($"{pet.Id}: el modo juego cancela también el final tardío desde {origin}",
                late.Activity == PetActivity.Hidden && late.Reaction == PetReaction.None);
        }
    }

    /// La capibara: sentada cuando no hace nada y, al arrancar Ollama, el paseo en cocodrilo entero.
    private static void Crocodile(Action<string, bool> check)
    {
        var capy = PetProfiles.Capybara;
        IEnumerable<PetPose> Sample(PetClip clip, int steps = 60) => Enumerable.Range(0, steps + 1).Select(i => clip.At(clip.Length * i / steps));
        check("capibara: en reposo, sentada (también con el pajarito)", Sample(capy.For(PetActivity.Alert)).All(p => p.Sit >= 0.7)
            && Sample(capy.Gesture(PetGesture.Bird)).All(p => p.Sit >= 0.7) && capy.Still(PetActivity.Alert).Sit >= 0.7);
        check("capibara: camina de pie", capy.Walk(3, 1, 0, false).Sit == 0);

        var mount = capy.Transition(PetActivity.DeepSleep, PetActivity.WakingUp, false, false)!;
        check("cocodrilo: sale de la tina, llega el cocodrilo y se sube", mount.At(0) is { BedIn: > 0.9, Croc: 0 }
            && mount.At(mount.Length) is { Ride: 1, Croc: 1, BedIn: 0 }
            && Sample(mount).Any(p => p is { BedIn: < 0.1, Ride: < 0.1, Croc: > 0.4 and < 0.9 }));
        // Lo espera de pie, salta de pie y se sienta ya encima; al bajar, al revés.
        var awakeMount = capy.Transition(PetActivity.Alert, PetActivity.WakingUp, false, false)!;
        bool Standing(PetClip clip, Func<PetPose, bool> when) => Sample(clip, 240).Where(when).All(p => p.Sit < 0.5);
        check("cocodrilo: lo espera de pie y salta de pie", Standing(mount, p => p.Croc is > 0.05 and < 0.95 && p.BedIn < 0.1)
            && Standing(mount, p => p.Ride is > 0.02 and < 0.98) && Standing(awakeMount, p => p.Croc is > 0.05 and < 0.95)
            && Standing(awakeMount, p => p.Ride is > 0.02 and < 0.98)
            && Sample(awakeMount, 240).Any(p => p is { Ride: 1, Sit: < 0.1 }) && awakeMount.At(awakeMount.Length) is { Ride: 1, Sit: >= 0.7 });
        var ride = Sample(capy.For(PetActivity.WakingUp)).ToList();
        check("cocodrilo: pasean, con el cocodrilo dando pasos", ride.All(p => p is { Ride: 1, Croc: 1 })
            && ride.Min(p => p.CrocStep) < -0.9 && ride.Max(p => p.CrocStep) > 0.9);
        var dismount = capy.Transition(PetActivity.WakingUp, PetActivity.Alert, false, false)!;
        check("cocodrilo: se baja y el cocodrilo se va", dismount.At(0).Ride == 1
            && dismount.At(dismount.Length) is { Ride: 0, Croc: 0 } && dismount.At(dismount.Length).Sit >= 0.7);
        check("cocodrilo: se levanta en el lomo, baja de pie y lo ve irse de pie", Standing(dismount, p => p.Ride is > 0.02 and < 0.98)
            && Sample(dismount, 240).Any(p => p is { Ride: 1, Sit: < 0.1 }) && Standing(dismount, p => p.Ride == 0 && p.Croc is > 0.05 and < 0.95));
        var back = capy.Transition(PetActivity.WakingUp, PetActivity.DeepSleep, false, false)!;
        check("cocodrilo: si Ollama no arranca, vuelve a la tina", back.At(back.Length).BedIn > 0.9 && back.At(back.Length).Croc == 0);
        check("cocodrilo: solo la capibara", PetCatalog.All.Where(p => p.Id != "capybara")
            .All(p => p.Animations.HoldFor(PetActivity.DeepSleep, PetActivity.WakingUp) == 0
                && Sample(p.Animations.For(PetActivity.WakingUp)).All(q => q.Croc == 0)));

        // Ollama arranca antes de que acabe el paseo: el paseo sigue hasta el final y después se baja.
        double hold = capy.HoldFor(PetActivity.DeepSleep, PetActivity.WakingUp);
        var brain = new PetBrain(capy);
        brain.Update(new(), 0);
        brain.Update(new(Starting: true), 10);
        brain.Update(new(Up: true), 11);
        check("cerebro: el paseo se ve entero aunque Ollama ya esté encendido", hold >= mount.Length + capy.For(PetActivity.WakingUp).Length - 1e-9
            && brain.Activity == PetActivity.WakingUp);
        brain.Expire(10 + hold - 0.01);
        bool riding = brain.Activity == PetActivity.WakingUp;
        brain.Expire(10 + hold + 0.01);
        check("cerebro: al terminar el paseo pasa a lo que toca", riding && brain.Activity == PetActivity.Drowsy);
        brain.Update(new(Starting: true), 100);
        brain.Update(new(Up: true, Game: true), 100.5);
        check("cerebro: un juego corta el paseo", brain.Activity == PetActivity.Hidden);

        var pet = PetCatalog.Find("capybara");
        var with = new PixelCanvas(pet.Size.Width * 3, pet.Size.Height * 3);
        var without = new PixelCanvas(with.Width, with.Height);
        pet.Draw(with, capy.Still(PetActivity.WakingUp), 3);
        pet.Draw(without, capy.Still(PetActivity.WakingUp) with { Croc = 0 }, 3);
        check("cocodrilo: se dibuja y la capibara va encima", !with.Pixels.SequenceEqual(without.Pixels)
            && with.ToBgra().Chunk(4).All(px => px[0] <= px[3] && px[1] <= px[3] && px[2] <= px[3]));

        // De pie en el lomo: sus patas pisan el cocodrilo (no flotan ni lo atraviesan).
        var standing = new PixelCanvas(pet.Size.Width * 3, pet.Size.Height * 3);
        pet.Draw(standing, capy.Still(PetActivity.Alert) with { Sit = 0, Ride = 1, Croc = 0 }, 3);   // sin el cocodrilo: solo ella
        int lowest = Enumerable.Range(0, standing.Height).Last(y => Enumerable.Range(0, standing.Width).Any(x => standing.Pixels[y * standing.Width + x] != 0));
        check("cocodrilo: de pie en el lomo, con las patas sobre él", lowest / 3.0 < IsTargetSleeping.UI.Pets.Crocodile.Top + 5 && lowest / 3.0 > IsTargetSleeping.UI.Pets.Crocodile.Top + 2);
    }

    /// Mientras Ollama arranca no se vuelven a dormir: el desperezo es la transición desde la
    /// cama y el bucle ya va despierto. La capibara bosteza sentada antes de la tina.
    private static void Waking(Action<string, bool> check)
    {
        IEnumerable<PetPose> Sample(PetClip clip, int steps = 120) => Enumerable.Range(0, steps + 1).Select(i => clip.At(clip.Length * i / steps));
        foreach (var pet in PetCatalog.All.Where(p => p.Id != "capybara"))
        {
            var a = pet.Animations;
            check($"{pet.Id}: mientras arranca no se vuelve a dormir", Sample(a.For(PetActivity.WakingUp)).All(p => p.Eye >= 0.1 && p.BedIn == 0));
            check($"{pet.Id}: al despertar sale de la cama desperezándose", a.Transition(PetActivity.DeepSleep, PetActivity.WakingUp, false, false) is { } wake
                && wake.At(0).BedIn > 0.9 && Sample(wake).Any(p => p.Mouth > 0.8) && wake.At(wake.Length) == a.For(PetActivity.WakingUp).At(0));
        }
        var capy = PetProfiles.Capybara;
        var yawn = Sample(capy.For(PetActivity.Yawning)).ToList();
        check("capibara: bosteza sentada antes de meterse en la tina", yawn.All(p => p.Sit >= 0.7) && yawn.Any(p => p.Mouth > 0.9));
    }
}
