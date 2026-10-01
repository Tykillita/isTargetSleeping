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
            var allClips = Enum.GetValues<PetActivity>().Select(profile.For)
                .Concat(Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None).Select(profile.For))
                .Concat(Enum.GetValues<PetGesture>().Where(g => g != PetGesture.None).Select(profile.Gesture)).Append(profile.Peek).ToArray();
            check($"{id}: clips reutilizados y tiempos finitos", allClips.All(c => c.Length > 0 && double.IsFinite(c.Length)) && ReferenceEquals(profile.For(PetActivity.Alert), profile.For(PetActivity.Alert)));
            check($"{id}: continuidad a 60 fps", allClips.All(c =>
                Enumerable.Range(0, (int)Math.Ceiling(c.Length * 60) + 1).All(i => PetPose.Distance(c.At(i / 60.0), c.At((i + 1) / 60.0)) < 0.36)));
            check($"{id}: reacciones duran hasta su última clave", Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None)
                .All(r => !profile.For(r).Loop && Math.Abs(profile.ReactionDuration(r) - profile.For(r).Length) < 1e-9));
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
            double end = 1 + profile.ReactionDuration(PetReaction.Jump);
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
        check("personalidades: velocidades y esperas acordadas", PetProfiles.Llama.WalkSpeed == 14 && PetProfiles.Llama.MinWait == 6 && PetProfiles.Llama.MaxWait == 12
            && PetProfiles.Capybara.WalkSpeed == 7 && PetProfiles.Capybara.MinWait == 12 && PetProfiles.Capybara.MaxWait == 22
            && PetProfiles.OrangeCat.WalkSpeed == 18 && PetProfiles.OrangeCat.MinWait == 4 && PetProfiles.OrangeCat.MaxWait == 9);
        check("Mira conserva los clips originales", Enum.GetValues<PetActivity>().All(a => PetProfiles.Mira.For(a).Keys.SequenceEqual(PetAnimations.For(a).Keys)));
        Personalities(check);
    }

    /// Cada mascota se mueve, reacciona, trata al ratón y suelta partículas a su manera.
    private static void Personalities(Action<string, bool> check)
    {
        var profiles = PetCatalog.All.Select(p => (p.Id, P: p.Animations, p.Anchors)).ToList();

        // Distintas entre sí.
        var clicks = profiles.Select(p => p.P.For(PetReaction.Jump)).ToList();
        check("clic: cada una reacciona con su propio clip y duración",
            profiles.Select(p => Math.Round(p.P.ReactionDuration(PetReaction.Jump), 3)).Distinct().Count() == 4
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
        check("campos nuevos: se interpolan", PetPose.Lerp(new(), new(Jaw: 1, Arch: 1, Curl: 1, Pupil: 1, Bath: 1, Bird: 1, PeekY: 2, Lock: 1), 0.5)
            is { Jaw: 0.5, Arch: 0.5, Curl: 0.5, Pupil: 0.5, Bath: 0.5, Bird: 0.5, PeekY: 1, Lock: 0.5 });
    }
}
