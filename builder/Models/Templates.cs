using System.Collections.Generic;
using builder.Viewport;
using OpenTK.Mathematics;

namespace builder.Models;

/// <summary>What a template drops into the world: fresh parts plus a camera and time of day.</summary>
public sealed class TemplateScene
{
    public List<SceneObject> Objects { get; init; } = new();
    public Vector3 Camera { get; init; } = new(0, 1.5f, 8f);
    public float Yaw { get; init; } = -90f;
    public float Pitch { get; init; } = -10f;
    public float TimeOfDay { get; init; } = 10f;
    /// <summary>Avatar start (no gray block stored: the engine drops your
    /// Spawn.bp mesh here, gray fallback).</summary>
    public Vector3? SpawnAt { get; init; }
}

/// <summary>One entry in File > Templates. Build runs on every use, so every
/// start is a fresh, unshared set of objects.</summary>
public sealed record PlaceTemplate(string Name, string Glyph, string Description, System.Func<TemplateScene> Build);

/// <summary>Ready-made starting places (File > Templates). The new-place
/// default in <see cref="Viewport.StudioScene"/> reuses <see cref="Baseplate"/>.</summary>
public static class Templates
{
    public static IReadOnlyList<PlaceTemplate> All { get; } = new List<PlaceTemplate>
    {
        new("Showcase", "🏛", "A bright white showroom with numbered stations: new shapes, materials, particles, toys, lights and water — all labeled.", Showcase),
        new("Castle", "🏰", "Walled keep with cone towers, throne room, dragon pit, treasure, guards and torchlight.", Castle),
        new("Playground", "🏝", "A baseplate packed with toys: lava crossing, stairs, teleports, NPCs and marbles.", Playground),
        new("Obby Starter", "🧗", "A mini jump course over three platforms to a trophy island, with a coach.", ObbyStarter),
        new("Shape Gallery", "🔷", "Every shape on the grass with labels: all 17, including the new six and the rounded box.", ShapeGallery),
        new("NPC Village", "🏘", "Three huts, a campfire and three chatty villagers.", NpcVillage),
        new("Tower Climb", "🗼", "Crate stairs up to a trophy platform, guarded at the base.", TowerClimb),
        new("Moonlight Arena", "🌙", "Walled midnight arena, two brutes, dramatic lighting.", MoonlightArena),
        new("Interactables", "🎮", "Every toy in one place: checkpoint, bounce, blink bridge, conveyor, magnet, loop mover, spinner, smash crates, teleports, lava, pool and a rocket.", Interactables),
        new("Racing Circuit", "🏁", "Sprint track with boost strips, a lava jump, checkpoints and a lap timer at the finish gate.", RacingCircuit),
        new("Tower Defense", "🛡", "Raiders chase you down the lane: three zap towers thin them out. Defend the base.", TowerDefense),
        new("Sky Obby", "☁", "Vertical climb over a lava lake: blink bridges, a spinner, a mover ferry and checkpoints to the trophy.", SkyObby),
    };

    public static PlaceTemplate? ByName(string name)
    {
        foreach (var t in All)
            if (t.Name == name) return t;
        return null;
    }

    // ---------- small builders ----------

    private static SceneObject Part(string name, ShapeKind shape, Vector3 pos, Vector3 size,
        Color4 color, MaterialKind mat = MaterialKind.Plastic, bool anchored = false) => new()
        {
            Name = name, Shape = shape, Position = pos, Size = size,
            Color = color, Material = mat, Anchored = anchored,
        };

    private static SceneObject Board(string name, Vector3 pos, Vector3 size, Color4 color, string text, string face) =>
        new()
        {
            Name = name, Shape = ShapeKind.Block, Position = pos, Size = size,
            Color = color, Material = MaterialKind.SmoothPlastic, Anchored = true,
            Texts = { new TextLayer { Text = text, Face = face } },
        };

    /// <summary>Double-sided sign: same text on two opposite faces (default
    /// Front/Back; pass Left/Right for side-wall plaques).</summary>
    private static SceneObject Sign(string name, Vector3 pos, Vector3 size, Color4 color, string text,
        string textColor = "255, 255, 255", string faceA = "Front", string faceB = "Back") =>
        new()
        {
            Name = name, Shape = ShapeKind.Block, Position = pos, Size = size,
            Color = color, Material = MaterialKind.SmoothPlastic, Anchored = true,
            Texts =
            {
                new TextLayer { Text = text, Face = faceA, Color = textColor },
                new TextLayer { Text = text, Face = faceB, Color = textColor },
            },
        };

    /// <summary>Floating white exhibit label with dark text (showroom tags).</summary>
    private static SceneObject Tag(string name, Vector3 pos, string text, float w = 3f) =>
        Sign(name, pos, new Vector3(w, 1f, 0.15f),
            new Color4(0.96f, 0.96f, 0.97f, 1f), text, "30, 32, 45");

    /// <summary>Yellow display pad: thin slab under an exhibit.</summary>
    private static SceneObject ShowPad(string name, Vector3 pos, Vector3 size) =>
        Part(name, ShapeKind.Block, pos, size,
            new Color4(0.95f, 0.75f, 0.10f, 1f), MaterialKind.SmoothPlastic, anchored: true);

    private static SceneObject Pedestal(Vector3 pos) =>
        Part("Pedestal", ShapeKind.Block, pos, new Vector3(2.2f, 1f, 2.2f),
            new Color4(0.93f, 0.93f, 0.95f, 1f), MaterialKind.SmoothPlastic, anchored: true);

    private static SceneObject Npc(string name, Vector3 pos, Color4 color, NpcKind kind, string dialogue,
        float damage = 15f, float speed = 3f) => new()
        {
            Name = name, Shape = ShapeKind.Capsule, Position = pos, Size = new Vector3(1.5f, 1.5f, 1.5f),
            Color = color, IsNpc = true, NpcType = kind, NpcDialogue = dialogue,
            NpcDamage = damage, NpcSpeed = speed, NpcChatRange = 10f, NpcChatInterval = 5f,
        };

    private static SceneObject Lamp(Vector3 pos, Color4 color, float brightness, float range) => new()
    {
        Name = "Lamp", Shape = ShapeKind.None, Light = LightKind.Point,
        Position = pos, Size = new Vector3(1, 1, 1), Color = color,
        Brightness = brightness, Range = range, Anchored = true,
    };

    private static SceneObject Lava(string name, Vector3 pos, Vector3 size) => new()
    {
        Name = name, Shape = ShapeKind.Killbrick, Position = pos, Size = size,
        Color = new Color4(1f, 0.20f, 0.10f, 1f), Material = MaterialKind.Neon,
        Anchored = true, Damage = 100f,
    };

    private static SceneObject Pad(string name, Vector3 pos, Color4 color, string link) => new()
    {
        Name = name, Shape = ShapeKind.Block, Position = pos, Size = new Vector3(2, 0.3f, 2),
        Color = color, Material = MaterialKind.Neon, Anchored = true, TeleportLink = link,
    };

    private static SceneObject GrassBase() => new()
    {
        Name = "Baseplate", Shape = ShapeKind.Block,
        Position = new Vector3(0, -0.5f, 0), Size = new Vector3(32, 1, 32),
        Color = new Color4(111f / 255f, 206f / 255f, 39f / 255f, 1f), Material = MaterialKind.Grass,
        Tiling = 0.2f,
        Anchored = true,
    };

    private static SceneObject Trophy(Vector3 pos) => new()
    {
        Name = "Trophy", Shape = ShapeKind.Cone, Position = pos, Size = new Vector3(0.8f, 1, 0.8f),
        Color = new Color4(0.95f, 0.80f, 0.25f, 1f), Material = MaterialKind.Neon,
        Anchored = true,
    };

    // ---------- templates ----------

    public static TemplateScene Baseplate() => new()
    {
        SpawnAt = new Vector3(0, 0.1f, 0),
        Objects =
        [
            GrassBase(),
        ],
    };

    public static TemplateScene Playground() => new()
    {
        SpawnAt = new Vector3(0, 0.1f, 10),
        Objects =
        [
            GrassBase(),
            Lava("Lava", new Vector3(0, 0.25f, 2), new Vector3(20, 0.5f, 2)),
            Part("Plank", ShapeKind.Block, new Vector3(-6, 0.6f, 2), new Vector3(2, 0.2f, 4),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Stairs", ShapeKind.Stairs, new Vector3(-8, 1f, -1), new Vector3(3, 2, 4),
                new Color4(0.70f, 0.55f, 0.40f, 1f), MaterialKind.Brick, anchored: true),
            Part("Platform", ShapeKind.Block, new Vector3(-8, 1.75f, -6), new Vector3(6, 0.5f, 6),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            Lamp(new Vector3(-8, 5f, -6), new Color4(1f, 0.85f, 0.60f, 1f), 2f, 20f),
            Part("Ramp", ShapeKind.Wedge, new Vector3(6, 1f, -6), new Vector3(3, 2, 5),
                new Color4(0.70f, 0.40f, 0.90f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Ball", ShapeKind.Ball, new Vector3(6, 2.7f, -7.6f), new Vector3(2, 2, 2),
                new Color4(0.95f, 0.55f, 0.20f, 1f)),
            Pad("Pad A", new Vector3(10, 0.15f, 6), new Color4(0.20f, 0.90f, 0.90f, 1f), "Pad1"),
            Pad("Pad B", new Vector3(-11, 0.15f, 7), new Color4(0.90f, 0.30f, 0.90f, 1f), "Pad1"),
            Npc("Guide", new Vector3(3, 0.75f, 8), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Welcome to the playground!|Cross the plank, lava hurts.|The glowing pads teleport you.|Mind the grumpy one..."),
            Npc("Grump", new Vector3(-4, 0.75f, -8), new Color4(0.90f, 0.25f, 0.20f, 1f), NpcKind.Enemy,
                "Grr!|Come here!|You can't run!"),
            Part("Stone 1", ShapeKind.Block, new Vector3(0, 0.6f, 2), new Vector3(1.2f, 0.2f, 1.2f),
                new Color4(0.55f, 0.55f, 0.58f, 1f), MaterialKind.Granite, anchored: true),
            Part("Stone 2", ShapeKind.Block, new Vector3(3, 0.6f, 2), new Vector3(1.2f, 0.2f, 1.2f),
                new Color4(0.55f, 0.55f, 0.58f, 1f), MaterialKind.Granite, anchored: true),
            Board("Start Sign", new Vector3(-4, 0.9f, 13), new Vector3(3, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "START!", "Back"),
            Board("Lava Sign", new Vector3(6, 0.9f, 4.5f), new Vector3(4, 1.8f, 0.3f),
                new Color4(0.60f, 0.10f, 0.10f, 1f), "OUCH!", "Front"),
            Board("Pad Sign", new Vector3(12.5f, 0.9f, 6), new Vector3(3, 1.8f, 0.3f),
                new Color4(0.10f, 0.50f, 0.50f, 1f), "STEP IN!", "Left"),
            Part("Crate 1", ShapeKind.Block, new Vector3(12, 0.5f, 0), new Vector3(2, 1, 2),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Crate 2", ShapeKind.Block, new Vector3(12, 1.5f, 0), new Vector3(2, 1, 2),
                new Color4(0.55f, 0.38f, 0.22f, 1f), MaterialKind.Wood, anchored: true),
            Trophy(new Vector3(12, 2.5f, 0)),
            Part("Marble Red", ShapeKind.Ball, new Vector3(2, 0.5f, -2), new Vector3(1, 1, 1),
                new Color4(0.95f, 0.30f, 0.30f, 1f), MaterialKind.SmoothPlastic),
            Part("Marble Blue", ShapeKind.Ball, new Vector3(-2, 0.5f, -3), new Vector3(1, 1, 1),
                new Color4(0.30f, 0.50f, 0.95f, 1f), MaterialKind.SmoothPlastic),
            Npc("Chill", new Vector3(-8, 2.75f, -6), new Color4(0.40f, 0.60f, 0.95f, 1f), NpcKind.Friendly,
                "Nice view, huh?|The lamp never burns out.|I live up here now."),
        ],
    };

    public static TemplateScene ObbyStarter() => new()
    {
        SpawnAt = new Vector3(0, 0.1f, 12),
        Objects =
        [
            GrassBase(),
            Npc("Coach", new Vector3(3.5f, 0.75f, 10.5f), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Jump the platforms, champ!|Red means ouch.|Grab the trophy up top!"),
            Part("Step 1", ShapeKind.Block, new Vector3(-2.5f, 0.5f, 6), new Vector3(3, 0.5f, 3),
                new Color4(0.25f, 0.55f, 0.95f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Step 2", ShapeKind.Block, new Vector3(0, 1.5f, 3.2f), new Vector3(3, 0.5f, 3),
                new Color4(0.95f, 0.55f, 0.20f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Step 3", ShapeKind.Block, new Vector3(2.5f, 2.5f, 0.4f), new Vector3(3, 0.5f, 3),
                new Color4(0.70f, 0.40f, 0.90f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Lava("Lava Pool", new Vector3(7, 0.25f, -2), new Vector3(6, 0.5f, 6)),
            Part("Finish", ShapeKind.Block, new Vector3(0, 2f, -6), new Vector3(6, 0.5f, 6),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            Trophy(new Vector3(0, 2.75f, -6)),
            Npc("Champ", new Vector3(-1.5f, 3f, -6), new Color4(0.95f, 0.80f, 0.25f, 1f), NpcKind.Friendly,
                "You made it!|Speedrun time: very fast.|The lava sends regards."),
            Board("Obby Sign", new Vector3(-5, 0.9f, 13.5f), new Vector3(4, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "JUMP!", "Back"),
        ],
    };

    public static TemplateScene ShapeGallery() => new()
    {
        Camera = new Vector3(0, 7f, 22),
        Pitch = -18f,
        SpawnAt = new Vector3(0, 0.1f, 14),
        Objects =
        [
            GrassBase(),
            Board("Gallery Sign", new Vector3(0, 1.4f, 13), new Vector3(8, 2f, 0.3f),
                new Color4(0.20f, 0.35f, 0.80f, 1f), "SHAPES GALLERY", "Back"),
            ..Exhibit("Block", ShapeKind.Block, new Vector3(-12.5f, 1f, -8), new Vector3(2, 2, 2),
                new Color4(0.25f, 0.55f, 0.95f, 1f)),
            ..Exhibit("Ball", ShapeKind.Ball, new Vector3(-7.5f, 1f, -8), new Vector3(2, 2, 2),
                new Color4(0.95f, 0.55f, 0.20f, 1f)),
            ..Exhibit("Cylinder", ShapeKind.Cylinder, new Vector3(-2.5f, 1.2f, -8), new Vector3(1.6f, 2.4f, 1.6f),
                new Color4(0.30f, 0.80f, 0.35f, 1f), MaterialKind.Metal),
            ..Exhibit("Wedge", ShapeKind.Wedge, new Vector3(2.5f, 1f, -8), new Vector3(2.4f, 2f, 2f),
                new Color4(0.70f, 0.40f, 0.90f, 1f)),
            ..Exhibit("Cone", ShapeKind.Cone, new Vector3(7.5f, 1.2f, -8), new Vector3(1.8f, 2.4f, 1.8f),
                new Color4(0.95f, 0.30f, 0.45f, 1f), MaterialKind.Neon),
            ..Exhibit("Torus", ShapeKind.Torus, new Vector3(12.5f, 1.1f, -8), new Vector3(2.2f, 2.2f, 2.2f),
                new Color4(0.25f, 0.85f, 0.85f, 1f), MaterialKind.Glass),
            ..Exhibit("Capsule", ShapeKind.Capsule, new Vector3(-12.5f, 0.75f, -2), new Vector3(1.5f, 1.5f, 1.5f),
                new Color4(0.95f, 0.80f, 0.25f, 1f)),
            ..Exhibit("Pyramid", ShapeKind.Pyramid, new Vector3(-7.5f, 1f, -2), new Vector3(2.4f, 2f, 2.4f),
                new Color4(0.70f, 0.70f, 0.75f, 1f), MaterialKind.Metal),
            ..Exhibit("Corner Wedge", ShapeKind.CornerWedge, new Vector3(-2.5f, 1f, -2), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.30f, 0.70f, 0.90f, 1f)),
            ..Exhibit("Stairs", ShapeKind.Stairs, new Vector3(2.5f, 1f, -2), new Vector3(3, 2, 3.4f),
                new Color4(0.60f, 0.45f, 0.30f, 1f)),
            ..Exhibit("Half Ball", ShapeKind.HalfBall, new Vector3(7.5f, 1f, -2), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.95f, 0.45f, 0.65f, 1f)),
            ..Exhibit("Hollow Cylinder", ShapeKind.HollowCylinder, new Vector3(12.5f, 1f, -2), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.45f, 0.65f, 0.95f, 1f), MaterialKind.Marble),
            ..Exhibit("Bowl", ShapeKind.Bowl, new Vector3(-10f, 1f, 4), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.90f, 0.60f, 0.25f, 1f), MaterialKind.Wood),
            ..Exhibit("Arch", ShapeKind.Arch, new Vector3(-5f, 1.2f, 4), new Vector3(3, 2.4f, 1.6f),
                new Color4(0.75f, 0.75f, 0.80f, 1f), MaterialKind.Marble),
            ..Exhibit("Hex Prism", ShapeKind.HexPrism, new Vector3(0f, 1f, 4), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.40f, 0.85f, 0.60f, 1f)),
            ..Exhibit("Truss", ShapeKind.Truss, new Vector3(5f, 1f, 4), new Vector3(2.2f, 2f, 2.2f),
                new Color4(0.60f, 0.50f, 0.40f, 1f), MaterialKind.Wood),
            ..Exhibit("Rounded Box", ShapeKind.RoundedBox, new Vector3(10f, 1f, 4), new Vector3(2, 2, 2),
                new Color4(1f, 0.84f, 0.35f, 1f)),
        ],
    };

    /// <summary>One gallery exhibit: the anchored shape sitting on the grass
    /// plus its floating label tag above.</summary>
    private static IEnumerable<SceneObject> Exhibit(string name, ShapeKind shape, Vector3 pos, Vector3 size,
        Color4 color, MaterialKind mat = MaterialKind.SmoothPlastic)
    {
        yield return Part(name, shape, pos, size, color, mat, anchored: true);
        yield return Tag(name + " Tag", new Vector3(pos.X, pos.Y + size.Y / 2f + 1f, pos.Z), name);
    }

    public static TemplateScene NpcVillage() => new()
    {
        SpawnAt = new Vector3(0, 0.1f, 12),
        Objects =
        [
            GrassBase(),
            Part("Hut 1", ShapeKind.Block, new Vector3(-8, 1.25f, 2), new Vector3(4, 2.5f, 4),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Roof 1", ShapeKind.Pyramid, new Vector3(-8, 3.3f, 2), new Vector3(4.8f, 1.6f, 4.8f),
                new Color4(0.65f, 0.25f, 0.20f, 1f), MaterialKind.Brick, anchored: true),
            Part("Hut 2", ShapeKind.Block, new Vector3(8, 1.25f, 2), new Vector3(4, 2.5f, 4),
                new Color4(0.55f, 0.45f, 0.30f, 1f), MaterialKind.Wood, anchored: true),
            Part("Roof 2", ShapeKind.Pyramid, new Vector3(8, 3.3f, 2), new Vector3(4.8f, 1.6f, 4.8f),
                new Color4(0.30f, 0.45f, 0.65f, 1f), MaterialKind.Brick, anchored: true),
            Part("Hut 3", ShapeKind.Block, new Vector3(0, 1.25f, -8), new Vector3(4, 2.5f, 4),
                new Color4(0.62f, 0.48f, 0.28f, 1f), MaterialKind.Wood, anchored: true),
            Part("Roof 3", ShapeKind.Pyramid, new Vector3(0, 3.3f, -8), new Vector3(4.8f, 1.6f, 4.8f),
                new Color4(0.55f, 0.30f, 0.55f, 1f), MaterialKind.Brick, anchored: true),
            Part("Campfire", ShapeKind.Cone, new Vector3(0, 0.5f, 4), new Vector3(1, 1, 1),
                new Color4(1f, 0.45f, 0.10f, 1f), MaterialKind.Neon, anchored: true),
            Lamp(new Vector3(0, 2.5f, 4), new Color4(1f, 0.70f, 0.40f, 1f), 2f, 14f),
            Npc("Elder", new Vector3(-5, 0.75f, 3.5f), new Color4(0.75f, 0.70f, 0.85f, 1f), NpcKind.Friendly,
                "Welcome to Willow Bend.|The fire never goes out.|Mind the well. There is no well. Yet."),
            Npc("Pip", new Vector3(5, 0.75f, 3.5f), new Color4(0.95f, 0.65f, 0.30f, 1f), NpcKind.Friendly,
                "I can run SO fast!|Race you to the big hut!|...you win."),
            Npc("Fisher", new Vector3(2.5f, 0.75f, -5.5f), new Color4(0.35f, 0.60f, 0.85f, 1f), NpcKind.Friendly,
                "Shh. I'm fishing.|...in grass.|Don't tell Elder."),
        ],
    };

    public static TemplateScene TowerClimb() => new()
    {
        Camera = new Vector3(0, 3.5f, 17),
        Pitch = -12f,
        SpawnAt = new Vector3(6, 0.1f, 11),
        Objects =
        [
            GrassBase(),
            Part("Crate 1", ShapeKind.Block, new Vector3(0, 0.5f, 8), new Vector3(2, 1, 2),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Crate 2", ShapeKind.Block, new Vector3(2, 1.5f, 5.5f), new Vector3(2, 1, 2),
                new Color4(0.56f, 0.39f, 0.23f, 1f), MaterialKind.Wood, anchored: true),
            Part("Crate 3", ShapeKind.Block, new Vector3(0, 2.5f, 3), new Vector3(2, 1, 2),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Crate 4", ShapeKind.Block, new Vector3(-2, 3.5f, 0.5f), new Vector3(2, 1, 2),
                new Color4(0.56f, 0.39f, 0.23f, 1f), MaterialKind.Wood, anchored: true),
            Part("Crate 5", ShapeKind.Block, new Vector3(0, 4.5f, -2), new Vector3(2, 1, 2),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood, anchored: true),
            Part("Top", ShapeKind.Block, new Vector3(0, 5.25f, -5.5f), new Vector3(4, 0.5f, 4),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            Trophy(new Vector3(0, 6f, -5.5f)),
            Npc("Guardian", new Vector3(-4, 0.75f, 9), new Color4(0.85f, 0.30f, 0.20f, 1f), NpcKind.Enemy,
                "None shall pass!|...okay, one shall pass.|Take the crates, climber.", damage: 10f, speed: 2.5f),
            Board("Tower Sign", new Vector3(7.5f, 0.9f, 13.5f), new Vector3(4, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "CLIMB!", "Back"),
        ],
    };

    public static TemplateScene MoonlightArena() => new()
    {
        Camera = new Vector3(0, 5f, 18),
        Pitch = -15f,
        TimeOfDay = 0f,
        SpawnAt = new Vector3(0, 0.1f, 5),
        Objects =
        [
            Part("Baseplate", ShapeKind.Block, new Vector3(0, -0.5f, 0), new Vector3(32, 1, 32),
                new Color4(0.35f, 0.35f, 0.38f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Wall N", ShapeKind.Block, new Vector3(0, 1.5f, -8), new Vector3(18, 3, 1),
                new Color4(0.50f, 0.30f, 0.30f, 1f), MaterialKind.Brick, anchored: true),
            Part("Wall S", ShapeKind.Block, new Vector3(0, 1.5f, 8), new Vector3(18, 3, 1),
                new Color4(0.50f, 0.30f, 0.30f, 1f), MaterialKind.Brick, anchored: true),
            Part("Wall W", ShapeKind.Block, new Vector3(-8, 1.5f, 0), new Vector3(1, 3, 15),
                new Color4(0.50f, 0.30f, 0.30f, 1f), MaterialKind.Brick, anchored: true),
            Part("Wall E", ShapeKind.Block, new Vector3(8, 1.5f, 0), new Vector3(1, 3, 15),
                new Color4(0.50f, 0.30f, 0.30f, 1f), MaterialKind.Brick, anchored: true),
            Lamp(new Vector3(-5, 4f, 0), new Color4(0.50f, 0.70f, 1f, 1f), 2.5f, 22f),
            Lamp(new Vector3(5, 4f, 0), new Color4(1f, 0.80f, 0.55f, 1f), 2.5f, 22f),
            Npc("Brutus", new Vector3(-4, 0.75f, -3), new Color4(0.80f, 0.15f, 0.15f, 1f), NpcKind.Enemy,
                "RAWR!|Moonlight makes me stronger!|It does not, but RAWR!", damage: 20f, speed: 4f),
            Npc("Fang", new Vector3(4, 0.75f, -3), new Color4(0.95f, 0.50f, 0.15f, 1f), NpcKind.Enemy,
                "Night night!|Quick feet, quick teeth!|Over here!", damage: 15f, speed: 5f),
            Npc("Coach", new Vector3(-5, 0.75f, 5), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Midnight sparring!|Two brutes, one moon.|Good luck!"),
        ],
    };

    public static TemplateScene Interactables() => new()
    {
        Camera = new Vector3(0, 7f, 24),
        Pitch = -20f,
        SpawnAt = new Vector3(0, 0.1f, 12),
        Objects =
        [
            GrassBase(),
            Board("Start Sign", new Vector3(-4.5f, 0.9f, 13.5f), new Vector3(5, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "TOUCH IT ALL!", "Back"),
            Npc("Coach", new Vector3(3.5f, 0.75f, 10.5f), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Touch everything!|Pads bounce you, pads zap you.|The pool is swimmable, I checked."),
            // 1 checkpoint
            new SceneObject
            {
                Name = "Checkpoint 1", Shape = ShapeKind.Block, Position = new Vector3(-10, 0.2f, 8),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            Board("Sign 1", new Vector3(-10, 1.2f, 10.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.55f, 0.50f, 0.20f, 1f), "1 CHECKPOINT", "Back"),
            // 2 bounce pad
            new SceneObject
            {
                Name = "Bounce", Shape = ShapeKind.Block, Position = new Vector3(-5, 0.2f, 8),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(0.20f, 0.90f, 0.90f, 1f),
                Material = MaterialKind.Neon, Anchored = true, IsBouncePad = true, BouncePower = 20f,
            },
            Board("Sign 2", new Vector3(-5, 1.2f, 10.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.50f, 0.50f, 1f), "2 BOUNCE", "Back"),
            // 3 blink bridge over lava
            Lava("Lava Pit", new Vector3(0, 0.1f, 8), new Vector3(4.5f, 0.2f, 4.5f)),
            new SceneObject
            {
                Name = "Blink Bridge", Shape = ShapeKind.Block, Position = new Vector3(0, 0.6f, 8),
                Size = new Vector3(4, 0.5f, 4), Color = new Color4(0.55f, 0.55f, 0.60f, 1f),
                Material = MaterialKind.Marble, Anchored = true,
                IsTimedPart = true, TimedHidden = 1f, TimedOffset = 0f, TimedVisible = 1f,
            },
            Board("Sign 3", new Vector3(0, 1.6f, 11f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), "3 BLINK", "Back"),
            // 4 conveyor ride with a crate on it
            new SceneObject
            {
                Name = "Belt", Shape = ShapeKind.Block, Position = new Vector3(6, 0.25f, 8),
                Size = new Vector3(6, 0.5f, 6), Color = new Color4(0.60f, 0.60f, 0.65f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsConveyor = true, ConveyorDirection = new Vector3(1, 0, 0), ConveyorSpeed = 8f,
            },
            Part("Belt Crate", ShapeKind.Block, new Vector3(4.5f, 1f, 8), new Vector3(1, 1, 1),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood),
            Board("Sign 4", new Vector3(6, 1.4f, 11.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), "4 RIDE", "Back"),
            // 5 magnet with marbles to steal
            new SceneObject
            {
                Name = "Magnet", Shape = ShapeKind.Ball, Position = new Vector3(11, 1f, 5),
                Size = new Vector3(2, 2, 2), Color = new Color4(1f, 0.20f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsMagnet = true, MagnetPower = 15f, MagnetRange = 10f,
            },
            Part("Marble A", ShapeKind.Ball, new Vector3(8.5f, 0.5f, 5), new Vector3(1, 1, 1),
                new Color4(0.30f, 0.50f, 0.95f, 1f), MaterialKind.SmoothPlastic),
            Part("Marble B", ShapeKind.Ball, new Vector3(8.5f, 0.5f, 3.5f), new Vector3(1, 1, 1),
                new Color4(0.95f, 0.80f, 0.25f, 1f), MaterialKind.SmoothPlastic),
            Board("Sign 5", new Vector3(11, 2.6f, 8f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.20f, 0.20f, 1f), "5 PULL", "Back"),
            // 6 loop mover with a triangle track
            new SceneObject
            {
                Name = "Looper", Shape = ShapeKind.Block, Position = new Vector3(-8, 1f, -2),
                Size = new Vector3(4, 0.5f, 4), Color = new Color4(0.90f, 0.60f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsMovingPlatform = true, MoveSpeed = 2f,
                Waypoints =
                {
                    new Vector3(-8, 1f, -2), new Vector3(-2, 1f, -2), new Vector3(-5, 1f, 3),
                },
            },
            Board("Sign 6", new Vector3(-8, 2.2f, -5.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.40f, 0.15f, 1f), "6 LOOP", "Front"),
            // 7 spinner
            new SceneObject
            {
                Name = "Spinner", Shape = ShapeKind.Cylinder, Position = new Vector3(0, 0.25f, -2),
                Size = new Vector3(6, 0.5f, 6), Color = new Color4(0.60f, 0.40f, 0.90f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsSpinner = true, SpinSpeed = 45f,
            },
            Board("Sign 7", new Vector3(0, 1.4f, -5.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.30f, 0.65f, 1f), "7 SPIN", "Front"),
            // 8 smash crates
            new SceneObject
            {
                Name = "Smash 1", Shape = ShapeKind.Block, Position = new Vector3(8, 1f, -4),
                Size = new Vector3(2, 2, 2), Color = new Color4(0.65f, 0.45f, 0.25f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            new SceneObject
            {
                Name = "Smash 2", Shape = ShapeKind.Block, Position = new Vector3(8, 3f, -4),
                Size = new Vector3(2, 2, 2), Color = new Color4(0.60f, 0.42f, 0.25f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            Board("Sign 8", new Vector3(8, 4.6f, -4), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.50f, 0.35f, 0.20f, 1f), "8 SMASH", "Front"),
            // 9 teleport pair
            Pad("Pad A", new Vector3(-12, 0.15f, 6), new Color4(0.20f, 0.90f, 0.90f, 1f), "Fun1"),
            Pad("Pad B", new Vector3(12, 0.15f, -8), new Color4(0.90f, 0.30f, 0.90f, 1f), "Fun1"),
            Board("Sign 9", new Vector3(-12, 1.2f, 8.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.50f, 0.50f, 1f), "9 ZAP", "Back"),
            // 10 lava strip
            Lava("Lava Strip", new Vector3(0, 0.25f, -8), new Vector3(10, 0.5f, 3)),
            Board("Sign 10", new Vector3(0, 1.2f, -10.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.10f, 0.10f, 1f), "10 OUCH", "Front"),
            // 11 swim pool
            new SceneObject
            {
                Name = "Pool", Shape = ShapeKind.Block, Position = new Vector3(-6, 1f, -8),
                Size = new Vector3(6, 2f, 6), Color = new Color4(0.15f, 0.40f, 0.90f, 1f),
                Material = MaterialKind.SmoothPlastic, Transparency = 0.45f,
                Anchored = true, CanCollide = false, IsWater = true,
            },
            Board("Sign 11", new Vector3(-6, 2.6f, -11.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.30f, 0.70f, 1f), "11 SWIM", "Front"),
            // 12 rocket (flies off on Play)
            new SceneObject
            {
                Name = "Rocket", Shape = ShapeKind.Wedge, Position = new Vector3(4, 1f, -10),
                Size = new Vector3(1.5f, 1.5f, 1.5f), Color = new Color4(0.95f, 0.25f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = false,
                VelocityDirection = new Vector3(0, 1, 0.2f), VelocitySpeed = 12f,
            },
            Board("Sign 12", new Vector3(4, 2.2f, -12.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.15f, 0.15f, 1f), "12 LAUNCH", "Front"),
            // trophy island
            Part("Pedestal", ShapeKind.Block, new Vector3(0, 1f, -13), new Vector3(2, 2, 2),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            Trophy(new Vector3(0, 2.5f, -13)),
            Lamp(new Vector3(0, 5f, 0), new Color4(1f, 0.85f, 0.60f, 1f), 2f, 24f),
        ],
    };

    /// <summary>Flagship tour: flat grid floor packed with every engine
    /// feature — shapes, materials, particles, toys, NPCs — all labeled.</summary>
    public static TemplateScene Showcase() => new()
    {
        Camera = new Vector3(0, 9f, 30),
        Pitch = -20f,
        SpawnAt = new Vector3(0, 0.1f, 15),
        Objects =
        [
            // Grid floor (no walls, no trail: features packed together).
            Part("Floor", ShapeKind.Block, new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Grid, anchored: true),
            Sign("Welcome", new Vector3(0, 1.3f, 13), new Vector3(7, 1.8f, 0.3f),
                new Color4(0.90f, 0.45f, 0.10f, 1f), "SHOWCASE — TOUCH IT ALL!"),
            Npc("Guide", new Vector3(5, 0.75f, 13.5f), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Welcome to the showcase!|Walk north: shapes, materials, toys.|Orange signs explain everything.|The trophy waits at the far end."),
            // New shapes row.
            Part("Half Ball", ShapeKind.HalfBall, new Vector3(-12, 1f, 8), new Vector3(2, 2, 2),
                new Color4(0.95f, 0.55f, 0.20f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Hollow Cylinder", ShapeKind.HollowCylinder, new Vector3(-8, 1f, 8), new Vector3(2, 2, 2),
                new Color4(0.30f, 0.60f, 0.95f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Bowl", ShapeKind.Bowl, new Vector3(-4, 0.5f, 8), new Vector3(2, 2, 2),
                new Color4(0.90f, 0.90f, 0.95f, 1f), MaterialKind.Marble, anchored: true),
            Part("Arch", ShapeKind.Arch, new Vector3(4, 1f, 8), new Vector3(2, 2, 2),
                new Color4(0.70f, 0.55f, 0.40f, 1f), MaterialKind.Brick, anchored: true),
            Part("Hex Prism", ShapeKind.HexPrism, new Vector3(8, 1f, 8), new Vector3(2, 2, 2),
                new Color4(0.25f, 0.80f, 0.70f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Truss", ShapeKind.Truss, new Vector3(12, 1f, 8), new Vector3(2, 2, 2),
                new Color4(0.85f, 0.70f, 0.25f, 1f), MaterialKind.Metal, anchored: true),
            Part("Donut", ShapeKind.Torus, new Vector3(0, 1.1f, 8), new Vector3(2.4f, 2.4f, 2.4f),
                new Color4(0.20f, 0.80f, 0.75f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Sign("Shapes", new Vector3(0, 2.4f, 10.5f), new Vector3(7, 1.6f, 0.3f),
                new Color4(0.15f, 0.45f, 0.75f, 1f), "NEW SHAPES"),
            // Materials row.
            Part("Wood", ShapeKind.Block, new Vector3(-14, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Wood, anchored: true),
            Part("Brick", ShapeKind.Block, new Vector3(-10, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Brick, anchored: true),
            Part("Marble", ShapeKind.Block, new Vector3(-6, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Marble, anchored: true),
            Part("Granite", ShapeKind.Block, new Vector3(-2, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Granite, anchored: true),
            Part("Sand", ShapeKind.Block, new Vector3(2, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Sand, anchored: true),
            Part("Neon", ShapeKind.Block, new Vector3(6, 1f, 2), new Vector3(2, 2, 2),
                new Color4(1f, 1f, 1f, 1f), MaterialKind.Neon, anchored: true),
            Part("Glass", ShapeKind.Block, new Vector3(10, 1f, 2), new Vector3(2, 2, 2),
                new Color4(0.75f, 0.90f, 1f, 1f), MaterialKind.Glass, anchored: true),
            Part("Ice", ShapeKind.Block, new Vector3(14, 1f, 2), new Vector3(2, 2, 2),
                new Color4(0.80f, 0.95f, 1f, 1f), MaterialKind.Ice, anchored: true),
            Sign("Materials", new Vector3(0, 2.4f, 5f), new Vector3(9, 1.8f, 0.3f),
                new Color4(0.50f, 0.35f, 0.70f, 1f), "MATERIALS — REAL PHOTOS"),
            // Particles corner, east.
            new SceneObject
            {
                Name = "Fire", Shape = ShapeKind.Block, Position = new Vector3(17, 1f, -2),
                Size = new Vector3(1.5f, 1.5f, 1.5f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 120f, ParticleLifetime = 0.75f, ParticleSpeed = 4.5f,
                ParticleSize = 0.5f, ParticleSpread = 0.22f, ParticleGravity = -6f,
            },
            new SceneObject
            {
                Name = "Smoke", Shape = ShapeKind.Block, Position = new Vector3(17, 1f, 0.5f),
                Size = new Vector3(1.5f, 1.5f, 1.5f), Color = new Color4(0.55f, 0.55f, 0.58f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsEmitter = true, ParticlePreset = "Smoke",
                EmissionRate = 25f, ParticleLifetime = 3f, ParticleSpeed = 1.5f,
                ParticleSize = 1f, ParticleSpread = 0.35f, ParticleGravity = -1.2f,
            },
            new SceneObject
            {
                Name = "Sparkles", Shape = ShapeKind.Block, Position = new Vector3(17, 1f, 3),
                Size = new Vector3(1.5f, 1.5f, 1.5f), Color = new Color4(1f, 0.95f, 0.6f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Sparkles",
                EmissionRate = 40f, ParticleLifetime = 1.2f, ParticleSpeed = 3f,
                ParticleSize = 0.3f, ParticleSpread = 1f, ParticleGravity = 1f,
            },
            Sign("Particles", new Vector3(17, 2.2f, 5.5f), new Vector3(5, 1.6f, 0.3f),
                new Color4(0.90f, 0.50f, 0.15f, 1f), "PARTICLES"),
            // Conveyor + magnet, west.
            new SceneObject
            {
                Name = "Belt", Shape = ShapeKind.Block, Position = new Vector3(-14, 0.25f, -4),
                Size = new Vector3(6, 0.5f, 3), Color = new Color4(0.60f, 0.60f, 0.65f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsConveyor = true, ConveyorDirection = new Vector3(1, 0, 0), ConveyorSpeed = 8f,
            },
            Part("Belt Crate", ShapeKind.Block, new Vector3(-15.5f, 1f, -4), new Vector3(1, 1, 1),
                new Color4(0.60f, 0.42f, 0.25f, 1f), MaterialKind.Wood),
            Sign("Ride", new Vector3(-14, 1.6f, -1), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), "RIDE"),
            new SceneObject
            {
                Name = "Magnet", Shape = ShapeKind.Ball, Position = new Vector3(-7, 1f, -4),
                Size = new Vector3(2, 2, 2), Color = new Color4(1f, 0.20f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsMagnet = true, MagnetPower = 15f, MagnetRange = 10f,
            },
            Part("Marble A", ShapeKind.Ball, new Vector3(-9.5f, 0.5f, -4), new Vector3(1, 1, 1),
                new Color4(0.30f, 0.50f, 0.95f, 1f), MaterialKind.SmoothPlastic),
            Sign("Pull", new Vector3(-7, 2.6f, -1.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.20f, 0.20f, 1f), "PULL"),
            new SceneObject
            {
                Name = "Spinner", Shape = ShapeKind.Cylinder, Position = new Vector3(0, 0.25f, -4),
                Size = new Vector3(6, 0.5f, 6), Color = new Color4(0.60f, 0.40f, 0.90f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsSpinner = true, SpinSpeed = 45f,
            },
            Sign("Spin", new Vector3(0, 1.6f, -1), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.30f, 0.65f, 1f), "SPIN"),
            new SceneObject
            {
                Name = "Looper", Shape = ShapeKind.Block, Position = new Vector3(7, 1f, -4),
                Size = new Vector3(4, 0.5f, 4), Color = new Color4(0.90f, 0.60f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsMovingPlatform = true, MoveSpeed = 2f,
                Waypoints =
                {
                    new Vector3(7, 1f, -4), new Vector3(11, 1f, -4), new Vector3(9, 1f, 0),
                },
            },
            Sign("Loop", new Vector3(7, 2.2f, -1), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.40f, 0.15f, 1f), "LOOP"),
            new SceneObject
            {
                Name = "Smash 1", Shape = ShapeKind.Block, Position = new Vector3(13, 1f, -4),
                Size = new Vector3(2, 2, 2), Color = new Color4(0.65f, 0.45f, 0.25f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            new SceneObject
            {
                Name = "Smash 2", Shape = ShapeKind.Block, Position = new Vector3(13, 3f, -4),
                Size = new Vector3(2, 2, 2), Color = new Color4(0.60f, 0.42f, 0.25f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            Sign("Smash", new Vector3(13, 4.6f, -4), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.50f, 0.35f, 0.20f, 1f), "SMASH"),
            // Bounce, checkpoint, lava, blink.
            new SceneObject
            {
                Name = "Bounce", Shape = ShapeKind.Block, Position = new Vector3(-14, 0.2f, -10),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(0.20f, 0.90f, 0.90f, 1f),
                Material = MaterialKind.Neon, Anchored = true, IsBouncePad = true, BouncePower = 20f,
            },
            Sign("Bounce", new Vector3(-14, 1.2f, -7.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.50f, 0.50f, 1f), "BOUNCE"),
            new SceneObject
            {
                Name = "Checkpoint 1", Shape = ShapeKind.Block, Position = new Vector3(-10, 0.2f, -10),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            Sign("Checkpoint", new Vector3(-10, 1.2f, -7.5f), new Vector3(5, 1.4f, 0.3f),
                new Color4(0.55f, 0.50f, 0.20f, 1f), "CHECKPOINT"),
            Lava("Blink Lava", new Vector3(0, 0.1f, -10), new Vector3(4.5f, 0.2f, 4.5f)),
            new SceneObject
            {
                Name = "Blink Bridge", Shape = ShapeKind.Block, Position = new Vector3(0, 0.6f, -10),
                Size = new Vector3(4, 0.5f, 4), Color = new Color4(0.55f, 0.55f, 0.60f, 1f),
                Material = MaterialKind.Marble, Anchored = true,
                IsTimedPart = true, TimedHidden = 1f, TimedOffset = 0f, TimedVisible = 1f,
            },
            Sign("Blink", new Vector3(0, 1.8f, -7.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), "BLINK"),
            Npc("Grump", new Vector3(-4, 0.75f, -9), new Color4(0.90f, 0.25f, 0.20f, 1f), NpcKind.Enemy,
                "Grr!|Mind the lava.|You can't run!"),
            // Teleports, pool, rocket, lights, trophy.
            Pad("Pad A", new Vector3(-14, 0.15f, -16), new Color4(0.20f, 0.90f, 0.90f, 1f), "Tour1"),
            Pad("Pad B", new Vector3(14, 0.15f, -16), new Color4(0.90f, 0.30f, 0.90f, 1f), "Tour1"),
            Sign("Zap", new Vector3(-14, 1.2f, -13.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.50f, 0.50f, 1f), "ZAP"),
            new SceneObject
            {
                Name = "Pool", Shape = ShapeKind.Block, Position = new Vector3(-4, 1f, -16),
                Size = new Vector3(6, 2f, 6), Color = new Color4(0.15f, 0.40f, 0.90f, 1f),
                Material = MaterialKind.SmoothPlastic, Transparency = 0.45f,
                Anchored = true, CanCollide = false, IsWater = true,
            },
            Sign("Swim", new Vector3(-4, 2.6f, -13.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.10f, 0.30f, 0.70f, 1f), "SWIM"),
            new SceneObject
            {
                Name = "Rocket", Shape = ShapeKind.Wedge, Position = new Vector3(4, 1f, -16),
                Size = new Vector3(1.5f, 1.5f, 1.5f), Color = new Color4(0.95f, 0.25f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = false,
                VelocityDirection = new Vector3(0, 1, 0.2f), VelocitySpeed = 12f,
            },
            Sign("Launch", new Vector3(4, 2.2f, -13.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.15f, 0.15f, 1f), "LAUNCH"),
            Lamp(new Vector3(-10, 5f, -20), new Color4(1f, 0.75f, 0.45f, 1f), 2f, 22f),
            Lamp(new Vector3(10, 5f, -20), new Color4(0.55f, 0.75f, 1f, 1f), 2f, 22f),
            Lamp(new Vector3(0, 5f, -22), new Color4(1f, 0.60f, 0.80f, 1f), 2f, 24f),
            Part("Pedestal", ShapeKind.Block, new Vector3(0, 1f, -22), new Vector3(2, 2, 2),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            Trophy(new Vector3(0, 2.5f, -22)),
            Sign("Winner", new Vector3(0, 4f, -22), new Vector3(6, 1.8f, 0.3f),
                new Color4(0.85f, 0.65f, 0.15f, 1f), "YOU MADE IT!"),
            Npc("Chill", new Vector3(-4, 0.75f, -21), new Color4(0.40f, 0.60f, 0.95f, 1f), NpcKind.Friendly,
                "You toured it all!|Fire, smoke, sparkles — my favorites.|The trophy is real gold. Probably."),
        ],
    };

    /// <summary>Castle at dusk: curtain walls, cone towers, gate, throne,
    /// dragon pit, treasure, well, hay bounce, torches, guards and a king.</summary>
    public static TemplateScene Castle() => new()
    {
        Camera = new Vector3(0, 12f, 34),
        Pitch = -24f,
        TimeOfDay = 17f,
        SpawnAt = new Vector3(0, 0.1f, 20),
        Objects =
        [
            GrassBase(),
            // Red carpet to the gate.
            Part("Carpet", ShapeKind.Block, new Vector3(0, 0.06f, 14), new Vector3(3, 0.12f, 10),
                new Color4(0.70f, 0.10f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            // Curtain walls (gap = south gate) + corner towers with cone roofs.
            Part("Wall S1", ShapeKind.Block, new Vector3(-7.5f, 2f, 12), new Vector3(9, 4, 1),
                new Color4(0.50f, 0.50f, 0.55f, 1f), MaterialKind.Granite, anchored: true),
            Part("Wall S2", ShapeKind.Block, new Vector3(7.5f, 2f, 12), new Vector3(9, 4, 1),
                new Color4(0.50f, 0.50f, 0.55f, 1f), MaterialKind.Granite, anchored: true),
            Part("Wall N", ShapeKind.Block, new Vector3(0, 2f, -12), new Vector3(25, 4, 1),
                new Color4(0.50f, 0.50f, 0.55f, 1f), MaterialKind.Granite, anchored: true),
            Part("Wall W", ShapeKind.Block, new Vector3(-12, 2f, 0), new Vector3(1, 4, 25),
                new Color4(0.50f, 0.50f, 0.55f, 1f), MaterialKind.Granite, anchored: true),
            Part("Wall E", ShapeKind.Block, new Vector3(12, 2f, 0), new Vector3(1, 4, 25),
                new Color4(0.50f, 0.50f, 0.55f, 1f), MaterialKind.Granite, anchored: true),
            Part("Tower SW", ShapeKind.Cylinder, new Vector3(-12, 4.5f, 12), new Vector3(4.4f, 9, 4.4f),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Granite, anchored: true),
            Part("Tower SE", ShapeKind.Cylinder, new Vector3(12, 4.5f, 12), new Vector3(4.4f, 9, 4.4f),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Granite, anchored: true),
            Part("Tower NW", ShapeKind.Cylinder, new Vector3(-12, 4.5f, -12), new Vector3(4.4f, 9, 4.4f),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Granite, anchored: true),
            Part("Tower NE", ShapeKind.Cylinder, new Vector3(12, 4.5f, -12), new Vector3(4.4f, 9, 4.4f),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Granite, anchored: true),
            Part("Roof SW", ShapeKind.Cone, new Vector3(-12, 10.5f, 12), new Vector3(5, 3, 5),
                new Color4(0.60f, 0.15f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Roof SE", ShapeKind.Cone, new Vector3(12, 10.5f, 12), new Vector3(5, 3, 5),
                new Color4(0.60f, 0.15f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Roof NW", ShapeKind.Cone, new Vector3(-12, 10.5f, -12), new Vector3(5, 3, 5),
                new Color4(0.60f, 0.15f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Roof NE", ShapeKind.Cone, new Vector3(12, 10.5f, -12), new Vector3(5, 3, 5),
                new Color4(0.60f, 0.15f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            // Gate: pillars, lintel, sign.
            Part("Gate L", ShapeKind.Block, new Vector3(-3.5f, 2f, 12), new Vector3(1, 4, 1),
                new Color4(0.40f, 0.28f, 0.16f, 1f), MaterialKind.Wood, anchored: true),
            Part("Gate R", ShapeKind.Block, new Vector3(3.5f, 2f, 12), new Vector3(1, 4, 1),
                new Color4(0.40f, 0.28f, 0.16f, 1f), MaterialKind.Wood, anchored: true),
            Part("Gate Top", ShapeKind.Block, new Vector3(0, 4.2f, 12), new Vector3(8, 0.8f, 1.2f),
                new Color4(0.40f, 0.28f, 0.16f, 1f), MaterialKind.Wood, anchored: true),
            Sign("Gate Sign", new Vector3(0, 5.4f, 12), new Vector3(8, 1.6f, 0.3f),
                new Color4(0.25f, 0.20f, 0.35f, 1f), "YE OLDE CASTLE (est. Tuesday)"),
            // Keep with door + glowing windows.
            Part("Keep", ShapeKind.Block, new Vector3(0, 4f, -8), new Vector3(8, 8, 8),
                new Color4(0.52f, 0.52f, 0.57f, 1f), MaterialKind.Granite, anchored: true),
            Part("Keep Roof", ShapeKind.Cone, new Vector3(0, 9.5f, -8), new Vector3(9, 3, 9),
                new Color4(0.15f, 0.25f, 0.55f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Keep Door", ShapeKind.Block, new Vector3(0, 1f, -3.95f), new Vector3(2, 2, 0.1f),
                new Color4(0.30f, 0.20f, 0.12f, 1f), MaterialKind.Wood, anchored: true),
            Part("Win A", ShapeKind.Block, new Vector3(-2.5f, 6f, -3.95f), new Vector3(1, 1, 0.1f),
                new Color4(1f, 0.85f, 0.40f, 1f), MaterialKind.Neon, anchored: true),
            Part("Win B", ShapeKind.Block, new Vector3(2.5f, 6f, -3.95f), new Vector3(1, 1, 0.1f),
                new Color4(1f, 0.85f, 0.40f, 1f), MaterialKind.Neon, anchored: true),
            Part("Win C", ShapeKind.Block, new Vector3(-2.5f, 3f, -3.95f), new Vector3(1, 1, 0.1f),
                new Color4(1f, 0.85f, 0.40f, 1f), MaterialKind.Neon, anchored: true),
            Part("Win D", ShapeKind.Block, new Vector3(2.5f, 3f, -3.95f), new Vector3(1, 1, 0.1f),
                new Color4(1f, 0.85f, 0.40f, 1f), MaterialKind.Neon, anchored: true),
            // Courtyard slab + outdoor throne + king.
            Part("Court", ShapeKind.Block, new Vector3(0, 0.05f, 0), new Vector3(22, 0.2f, 20),
                new Color4(0.60f, 0.60f, 0.62f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Throne", ShapeKind.Block, new Vector3(0, 0.75f, -4), new Vector3(2, 1.5f, 1),
                new Color4(0.95f, 0.75f, 0.20f, 1f), MaterialKind.Metal, anchored: true),
            Part("Throne Back", ShapeKind.Block, new Vector3(0, 1.75f, -4.5f), new Vector3(2, 2.5f, 0.5f),
                new Color4(0.95f, 0.75f, 0.20f, 1f), MaterialKind.Metal, anchored: true),
            Sign("Throne Sign", new Vector3(0, 3.6f, -4), new Vector3(6, 1.4f, 0.3f),
                new Color4(0.70f, 0.55f, 0.15f, 1f), "SIT. HE DARES YOU."),
            Npc("King", new Vector3(2.5f, 0.75f, -4), new Color4(0.70f, 0.20f, 0.70f, 1f), NpcKind.Friendly,
                "I AM the king!|The crown? Plastic. Don't tell.|Peasants bow. It's the law. Probably.|Great sunburns out here."),
            // Treasure corner + guard.
            Part("Loot", ShapeKind.Block, new Vector3(8, 0.5f, -8), new Vector3(1.5f, 1f, 1.5f),
                new Color4(1f, 0.80f, 0.20f, 1f), MaterialKind.Neon, anchored: true),
            Trophy(new Vector3(8, 1.5f, -8)),
            Sign("Loot Sign", new Vector3(8, 2.8f, -8), new Vector3(6, 1.4f, 0.3f),
                new Color4(0.60f, 0.45f, 0.10f, 1f), "ROYAL PIGGY BANK (do not shake)"),
            Npc("Guard", new Vector3(10, 0.75f, -6), new Color4(0.30f, 0.45f, 0.85f, 1f), NpcKind.Friendly,
                "Halt! ...okay, walk in.|Nothing gets past me.|Except Gary. Gary got past me.|Gary works the night shift now."),
            // Dragon pit + lava.
            Lava("Dragon Pit", new Vector3(-8, 0.15f, -8), new Vector3(5, 0.3f, 5)),
            Npc("Dragon", new Vector3(-4.5f, 0.75f, -8), new Color4(0.20f, 0.65f, 0.30f, 1f), NpcKind.Enemy,
                "ROAR!|...that was my stomach. Lunch?|I accept tribute in tacos.|No tacos? Then PERISH."),
            Sign("Pit Sign", new Vector3(-8, 1.6f, -4.5f), new Vector3(5, 1.4f, 0.3f),
                new Color4(0.55f, 0.12f, 0.12f, 1f), "DRAGON. KNOCK FIRST."),
            // Wishing well (hollow ring!) + swim-light water.
            Part("Well Ring", ShapeKind.HollowCylinder, new Vector3(-8, 1f, 4), new Vector3(3, 2, 3),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Granite, anchored: true),
            Part("Well Water", ShapeKind.Cylinder, new Vector3(-8, 0.7f, 4), new Vector3(1.8f, 1.2f, 1.8f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), MaterialKind.Glass, anchored: true),
            Sign("Well Sign", new Vector3(-8, 2.6f, 4), new Vector3(5, 1.4f, 0.3f),
                new Color4(0.15f, 0.35f, 0.60f, 1f), "WISHING WELL (25¢)"),
            // Hay bounce + barrels.
            new SceneObject
            {
                Name = "Hay", Shape = ShapeKind.Block, Position = new Vector3(8, 0.2f, 4),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(0.90f, 0.80f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsBouncePad = true, BouncePower = 20f,
            },
            Part("Haystack", ShapeKind.Cone, new Vector3(8, 1.5f, 4), new Vector3(2, 2, 2),
                new Color4(0.85f, 0.70f, 0.25f, 1f), MaterialKind.Sand, anchored: true),
            Sign("Hay Sign", new Vector3(8, 3f, 4), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.60f, 0.50f, 0.15f, 1f), "HAY! (trust me)"),
            new SceneObject
            {
                Name = "Barrel 1", Shape = ShapeKind.Cylinder, Position = new Vector3(11, 1f, 2),
                Size = new Vector3(1.5f, 2, 1.5f), Color = new Color4(0.55f, 0.38f, 0.20f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            new SceneObject
            {
                Name = "Barrel 2", Shape = ShapeKind.Cylinder, Position = new Vector3(11, 3f, 2),
                Size = new Vector3(1.5f, 2, 1.5f), Color = new Color4(0.50f, 0.35f, 0.18f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            new SceneObject
            {
                Name = "Barrel 3", Shape = ShapeKind.Cylinder, Position = new Vector3(9, 1f, 2),
                Size = new Vector3(1.5f, 2, 1.5f), Color = new Color4(0.58f, 0.40f, 0.22f, 1f),
                Material = MaterialKind.Wood, IsBreakable = true, BreakHits = 2,
            },
            // Secret passage (throne to dungeon).
            Pad("Secret A", new Vector3(2.5f, 0.15f, -6), new Color4(0.40f, 0.20f, 0.70f, 1f), "Keep1"),
            Pad("Secret B", new Vector3(-10.5f, 0.15f, -10), new Color4(0.40f, 0.20f, 0.70f, 1f), "Keep1"),
            Sign("Secret Sign", new Vector3(2.5f, 1.2f, -7.5f), new Vector3(4, 1.4f, 0.3f),
                new Color4(0.35f, 0.20f, 0.55f, 1f), "SECRET?? (shh)"),
            // Torch posts: pole + lamp + live flame.
            Part("Torch Pole 1", ShapeKind.Cylinder, new Vector3(-9, 1.5f, 8), new Vector3(0.3f, 3, 0.3f),
                new Color4(0.30f, 0.22f, 0.14f, 1f), MaterialKind.Wood, anchored: true),
            Lamp(new Vector3(-9, 3.4f, 8), new Color4(1f, 0.60f, 0.20f, 1f), 2f, 14f),
            new SceneObject
            {
                Name = "Torch 1", Shape = ShapeKind.Block, Position = new Vector3(-9, 3.4f, 8),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
            Part("Torch Pole 2", ShapeKind.Cylinder, new Vector3(9, 1.5f, 8), new Vector3(0.3f, 3, 0.3f),
                new Color4(0.30f, 0.22f, 0.14f, 1f), MaterialKind.Wood, anchored: true),
            Lamp(new Vector3(9, 3.4f, 8), new Color4(1f, 0.60f, 0.20f, 1f), 2f, 14f),
            new SceneObject
            {
                Name = "Torch 2", Shape = ShapeKind.Block, Position = new Vector3(9, 3.4f, 8),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
            Part("Torch Pole 3", ShapeKind.Cylinder, new Vector3(-9, 1.5f, -4), new Vector3(0.3f, 3, 0.3f),
                new Color4(0.30f, 0.22f, 0.14f, 1f), MaterialKind.Wood, anchored: true),
            Lamp(new Vector3(-9, 3.4f, -4), new Color4(1f, 0.60f, 0.20f, 1f), 2f, 14f),
            new SceneObject
            {
                Name = "Torch 3", Shape = ShapeKind.Block, Position = new Vector3(-9, 3.4f, -4),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
            Part("Torch Pole 4", ShapeKind.Cylinder, new Vector3(9, 1.5f, -4), new Vector3(0.3f, 3, 0.3f),
                new Color4(0.30f, 0.22f, 0.14f, 1f), MaterialKind.Wood, anchored: true),
            Lamp(new Vector3(9, 3.4f, -4), new Color4(1f, 0.60f, 0.20f, 1f), 2f, 14f),
            new SceneObject
            {
                Name = "Torch 4", Shape = ShapeKind.Block, Position = new Vector3(9, 3.4f, -4),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
            // Banners + checkpoint at the gate.
            Part("Banner L", ShapeKind.Block, new Vector3(-6, 5f, 11.4f), new Vector3(2, 4, 0.1f),
                new Color4(0.70f, 0.12f, 0.12f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Banner R", ShapeKind.Block, new Vector3(6, 5f, 11.4f), new Vector3(2, 4, 0.1f),
                new Color4(0.15f, 0.25f, 0.60f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Sign("Banner L Sign", new Vector3(-6, 5f, 11.3f), new Vector3(1.8f, 1.2f, 0.05f),
                new Color4(0.70f, 0.12f, 0.12f, 1f), "KING"),
            Sign("Banner R Sign", new Vector3(6, 5f, 11.3f), new Vector3(1.8f, 1.2f, 0.05f),
                new Color4(0.15f, 0.25f, 0.60f, 1f), "GUARD"),
            new SceneObject
            {
                Name = "Gate Checkpoint", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, 10),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            Sign("Save Sign", new Vector3(0, 1.4f, 8f), new Vector3(5, 1.4f, 0.3f),
                new Color4(0.55f, 0.50f, 0.20f, 1f), "SAVE POINT (no refunds)"),
        ],
    };

    public static TemplateScene RacingCircuit() => new()
    {
        Camera = new Vector3(0, 7f, 24),
        Pitch = -18f,
        SpawnAt = new Vector3(0, 0.1f, 14),
        Objects =
        [
            GrassBase(),
            Board("Start Sign", new Vector3(-5.5f, 0.9f, 13), new Vector3(4, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "RACE TO THE CUP!", "Back"),
            Npc("Coach", new Vector3(4, 0.75f, 12.5f), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Run straight!|Blue strips boost you.|Bounce over the lava.|The gate times your lap."),
            // Start arch + respawn pad.
            Part("Arch L", ShapeKind.Block, new Vector3(-3, 1.5f, 12), new Vector3(0.6f, 3f, 0.6f),
                new Color4(0.95f, 0.30f, 0.30f, 1f), MaterialKind.Neon, anchored: true),
            Part("Arch R", ShapeKind.Block, new Vector3(3, 1.5f, 12), new Vector3(0.6f, 3f, 0.6f),
                new Color4(0.95f, 0.30f, 0.30f, 1f), MaterialKind.Neon, anchored: true),
            Board("Start Banner", new Vector3(0, 3.3f, 12), new Vector3(7, 1f, 0.3f),
                new Color4(0.80f, 0.12f, 0.12f, 1f), "START", "Back"),
            new SceneObject
            {
                Name = "Start", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, 12),
                Size = new Vector3(6, 0.4f, 2), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Boost strip 1 (conveyor toward -Z: local +X steered by yaw 90).
            new SceneObject
            {
                Name = "Boost 1", Shape = ShapeKind.Block, Position = new Vector3(0, 0.25f, 5),
                Size = new Vector3(4, 0.5f, 8), Color = new Color4(0.15f, 0.55f, 0.95f, 1f),
                Material = MaterialKind.Neon, Anchored = true, Rotation = new Vector3(0, 90f, 0),
                IsConveyor = true, ConveyorDirection = new Vector3(1, 0, 0), ConveyorSpeed = 12f,
            },
            new SceneObject
            {
                Name = "Checkpoint 1", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, -1),
                Size = new Vector3(4, 0.4f, 2), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Lava gap with a bounce pad before it.
            Lava("Lava Gap", new Vector3(0, 0.1f, -5.5f), new Vector3(10, 0.2f, 4)),
            new SceneObject
            {
                Name = "Jump Pad", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, -2.5f),
                Size = new Vector3(3, 0.4f, 2), Color = new Color4(0.20f, 0.90f, 0.90f, 1f),
                Material = MaterialKind.Neon, Anchored = true, IsBouncePad = true, BouncePower = 24f,
            },
            new SceneObject
            {
                Name = "Checkpoint 2", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, -9),
                Size = new Vector3(4, 0.4f, 2), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Boost strip 2, faster.
            new SceneObject
            {
                Name = "Boost 2", Shape = ShapeKind.Block, Position = new Vector3(0, 0.25f, -14),
                Size = new Vector3(4, 0.5f, 8), Color = new Color4(0.15f, 0.55f, 0.95f, 1f),
                Material = MaterialKind.Neon, Anchored = true, Rotation = new Vector3(0, 90f, 0),
                IsConveyor = true, ConveyorDirection = new Vector3(1, 0, 0), ConveyorSpeed = 14f,
            },
            // Finish arch, gate slab + lap timer script + trophy.
            Part("Finish L", ShapeKind.Block, new Vector3(-3, 1.5f, -20), new Vector3(0.6f, 3f, 0.6f),
                new Color4(0.95f, 0.80f, 0.25f, 1f), MaterialKind.Neon, anchored: true),
            Part("Finish R", ShapeKind.Block, new Vector3(3, 1.5f, -20), new Vector3(0.6f, 3f, 0.6f),
                new Color4(0.95f, 0.80f, 0.25f, 1f), MaterialKind.Neon, anchored: true),
            Board("Finish Banner", new Vector3(0, 3.3f, -20), new Vector3(7, 1f, 0.3f),
                new Color4(0.75f, 0.60f, 0.10f, 1f), "FINISH", "Back"),
            new SceneObject
            {
                Name = "Finish Gate", Shape = ShapeKind.Block, Position = new Vector3(0, 0.2f, -20),
                Size = new Vector3(6, 0.4f, 2), Color = new Color4(0.95f, 0.80f, 0.25f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
                Script = "laps = 0\nfunction onTouch(player)\n laps = laps + 1\n" +
                    " game:log(\"Lap \" .. laps .. \" at \" .. math.floor(game.time) .. \"s\")\n" +
                    " game:playSound(\"stomp\", 1)\nend\n",
            },
            Trophy(new Vector3(0, 0.8f, -23)),
            // Guard rails so racers stay on track.
            Part("Rail L", ShapeKind.Block, new Vector3(-5, 0.5f, -4), new Vector3(0.4f, 1f, 32),
                new Color4(0.80f, 0.80f, 0.85f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("Rail R", ShapeKind.Block, new Vector3(5, 0.5f, -4), new Vector3(0.4f, 1f, 32),
                new Color4(0.80f, 0.80f, 0.85f, 1f), MaterialKind.SmoothPlastic, anchored: true),
        ],
    };

    public static TemplateScene TowerDefense() => new()
    {
        Camera = new Vector3(0, 11f, 22),
        Pitch = -25f,
        SpawnAt = new Vector3(12, 0.1f, 6),
        Objects =
        [
            GrassBase(),
            Board("TD Sign", new Vector3(12, 1.2f, 9), new Vector3(7, 1.8f, 0.3f),
                new Color4(0.15f, 0.35f, 0.70f, 1f), "DEFEND THE BASE!", "Back"),
            Npc("Commander", new Vector3(12, 0.75f, 2), new Color4(0.25f, 0.55f, 0.95f, 1f), NpcKind.Friendly,
                "Raiders incoming!|My towers zap them close up.|Lure them down the lane.|Don't let them touch you."),
            // Funnel walls down the lane.
            Part("Wall N", ShapeKind.Block, new Vector3(0, 1f, -4), new Vector3(30, 2f, 0.6f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), MaterialKind.Brick, anchored: true),
            Part("Wall S", ShapeKind.Block, new Vector3(0, 1f, 4), new Vector3(30, 2f, 0.6f),
                new Color4(0.45f, 0.45f, 0.50f, 1f), MaterialKind.Brick, anchored: true),
            // Raider pen (west end).
            Part("Pen Gate", ShapeKind.Block, new Vector3(-13, 1f, 0), new Vector3(0.6f, 2f, 6),
                new Color4(0.60f, 0.15f, 0.15f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Npc("Raider 1", new Vector3(-11, 0.75f, -2), new Color4(0.85f, 0.20f, 0.15f, 1f), NpcKind.Enemy,
                "Rawr!|Base go boom!", damage: 10f, speed: 3.5f),
            Npc("Raider 2", new Vector3(-11, 0.75f, 0), new Color4(0.85f, 0.20f, 0.15f, 1f), NpcKind.Enemy,
                "Rawr!|Smash!", damage: 10f, speed: 3.5f),
            Npc("Raider 3", new Vector3(-11, 0.75f, 2), new Color4(0.85f, 0.20f, 0.15f, 1f), NpcKind.Enemy,
                "Rawr!|Charge!", damage: 10f, speed: 3.5f),
            Npc("Raider 4", new Vector3(-13, 0.75f, -1), new Color4(0.90f, 0.35f, 0.10f, 1f), NpcKind.Enemy,
                "Rawr!|Faster!", damage: 12f, speed: 4.5f),
            Npc("Raider 5", new Vector3(-13, 0.75f, 1), new Color4(0.90f, 0.35f, 0.10f, 1f), NpcKind.Enemy,
                "Rawr!|No mercy!", damage: 12f, speed: 4.5f),
            Npc("Raider 6", new Vector3(-14, 0.75f, 0), new Color4(0.95f, 0.55f, 0.10f, 1f), NpcKind.Enemy,
                "BOSS!|You are done!", damage: 20f, speed: 3f),
            // Zap towers flanking the lane (10-stud range, shared script).
            Tower("Tower 1", new Vector3(-4, 1.5f, -5.5f)),
            Tower("Tower 2", new Vector3(2, 1.5f, 5.5f)),
            Tower("Tower 3", new Vector3(8, 1.5f, -5.5f)),
            // Base pad + trophy for the last raider standing... you.
            new SceneObject
            {
                Name = "Base", Shape = ShapeKind.Block, Position = new Vector3(12, 0.2f, 0),
                Size = new Vector3(4, 0.4f, 4), Color = new Color4(0.20f, 0.45f, 0.95f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            Trophy(new Vector3(12, 1f, -2.5f)),
            new SceneObject
            {
                Name = "Torch 1", Shape = ShapeKind.Block, Position = new Vector3(-6, 2.6f, -4),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
            new SceneObject
            {
                Name = "Torch 2", Shape = ShapeKind.Block, Position = new Vector3(6, 2.6f, 4),
                Size = new Vector3(0.6f, 0.6f, 0.6f), Color = new Color4(1f, 0.45f, 0.1f, 1f),
                Material = MaterialKind.Neon, Anchored = true,
                IsEmitter = true, ParticlePreset = "Fire",
                EmissionRate = 60f, ParticleLifetime = 0.6f, ParticleSpeed = 2.5f,
                ParticleSize = 0.35f, ParticleSpread = 0.15f, ParticleGravity = -4f,
            },
        ],
    };

    /// <summary>Zap tower: destroys Raiders 1-6 inside 10 studs, keeps score.</summary>
    private static SceneObject Tower(string name, Vector3 pos) => new()
    {
        Name = name, Shape = ShapeKind.Block, Position = pos, Size = new Vector3(1.5f, 3f, 1.5f),
        Color = new Color4(0.30f, 0.75f, 0.95f, 1f), Material = MaterialKind.Neon, Anchored = true,
        Script = "kills = 0\nfunction onTick(dt)\n for i = 1, 6 do\n" +
            "  local e = game:findPart(\"Raider \" .. i)\n  if e ~= nil then\n" +
            "   local dx = e.Position.x - part.Position.x\n   local dz = e.Position.z - part.Position.z\n" +
            "   if dx * dx + dz * dz < 100 then\n    e:destroy()\n    kills = kills + 1\n" +
            "    game:playSound(\"stomp\", 1)\n    game:log(\"Tower zapped Raider \" .. i .. \" (\" .. kills .. \")\")\n" +
            "   end\n  end\n end\nend\n",
    };

    public static TemplateScene SkyObby() => new()
    {
        Camera = new Vector3(0, 10f, 26),
        Pitch = -20f,
        SpawnAt = new Vector3(0, 2.6f, 8),
        Objects =
        [
            // Lava lake far below: falling costs a respawn, not a swim.
            Lava("Lava Lake", new Vector3(0, -6f, 0), new Vector3(44, 0.5f, 44)),
            Board("Climb Sign", new Vector3(-5.5f, 3f, 8), new Vector3(5, 1.8f, 0.3f),
                new Color4(0.20f, 0.50f, 0.95f, 1f), "CLIMB!", "Back"),
            Npc("Coach", new Vector3(3, 2.75f, 9), new Color4(0.30f, 0.80f, 0.45f, 1f), NpcKind.Friendly,
                "Up we go!|Yellow pads save you.|Blink bridges need timing.|The cup waits at the top."),
            // Start deck.
            Part("Start Deck", ShapeKind.Block, new Vector3(0, 2f, 8), new Vector3(8, 0.5f, 8),
                new Color4(0.55f, 0.55f, 0.60f, 1f), MaterialKind.Marble, anchored: true),
            new SceneObject
            {
                Name = "Checkpoint 0", Shape = ShapeKind.Block, Position = new Vector3(0, 2.45f, 8),
                Size = new Vector3(3, 0.4f, 3), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Tier 1: plain hops.
            Part("T1 A", ShapeKind.Block, new Vector3(6, 3.5f, 4), new Vector3(3, 0.5f, 3),
                new Color4(0.55f, 0.70f, 0.95f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            Part("T1 B", ShapeKind.Block, new Vector3(1, 5f, 0), new Vector3(3, 0.5f, 3),
                new Color4(0.55f, 0.70f, 0.95f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            new SceneObject
            {
                Name = "Checkpoint 1", Shape = ShapeKind.Block, Position = new Vector3(1, 5.45f, 0),
                Size = new Vector3(2.5f, 0.4f, 2.5f), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Tier 2: blink bridge over the void.
            Part("T2 A", ShapeKind.Block, new Vector3(-4, 6.5f, -3), new Vector3(3, 0.5f, 3),
                new Color4(0.60f, 0.45f, 0.85f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            new SceneObject
            {
                Name = "Blink", Shape = ShapeKind.Block, Position = new Vector3(-4, 7f, -7),
                Size = new Vector3(3, 0.5f, 5), Color = new Color4(0.55f, 0.55f, 0.60f, 1f),
                Material = MaterialKind.Marble, Anchored = true,
                IsTimedPart = true, TimedHidden = 1f, TimedOffset = 0f, TimedVisible = 1.5f,
            },
            Part("T2 B", ShapeKind.Block, new Vector3(-4, 7.5f, -11), new Vector3(3, 0.5f, 3),
                new Color4(0.60f, 0.45f, 0.85f, 1f), MaterialKind.SmoothPlastic, anchored: true),
            new SceneObject
            {
                Name = "Checkpoint 2", Shape = ShapeKind.Block, Position = new Vector3(-4, 7.95f, -11),
                Size = new Vector3(2.5f, 0.4f, 2.5f), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Tier 3: spinner disc (slow) + mover ferry.
            new SceneObject
            {
                Name = "Spinner", Shape = ShapeKind.Cylinder, Position = new Vector3(1, 8.5f, -13),
                Size = new Vector3(6, 0.5f, 6), Color = new Color4(0.90f, 0.55f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsSpinner = true, SpinSpeed = 30f,
            },
            new SceneObject
            {
                Name = "Ferry", Shape = ShapeKind.Block, Position = new Vector3(7, 9.5f, -13),
                Size = new Vector3(3, 0.5f, 3), Color = new Color4(0.90f, 0.60f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true,
                IsMovingPlatform = true, MoveSpeed = 2.5f,
                Waypoints = { new Vector3(7, 9.5f, -13), new Vector3(7, 9.5f, -20) },
            },
            Part("T3 Top", ShapeKind.Block, new Vector3(7, 10f, -22), new Vector3(3.5f, 0.5f, 3.5f),
                new Color4(0.45f, 0.80f, 0.55f, 1f), MaterialKind.Grass, anchored: true),
            new SceneObject
            {
                Name = "Checkpoint 3", Shape = ShapeKind.Block, Position = new Vector3(7, 10.45f, -22),
                Size = new Vector3(2.5f, 0.4f, 2.5f), Color = new Color4(1f, 0.85f, 0.20f, 1f),
                Material = MaterialKind.SmoothPlastic, Anchored = true, IsCheckpoint = true,
            },
            // Tier 4: bounce up to the summit.
            new SceneObject
            {
                Name = "Up Pad", Shape = ShapeKind.Block, Position = new Vector3(7, 10.7f, -22),
                Size = new Vector3(2, 0.4f, 2), Color = new Color4(0.20f, 0.90f, 0.90f, 1f),
                Material = MaterialKind.Neon, Anchored = true, IsBouncePad = true, BouncePower = 26f,
            },
            Part("Summit", ShapeKind.Block, new Vector3(2, 14f, -24), new Vector3(6, 0.5f, 6),
                new Color4(0.96f, 0.93f, 0.80f, 1f), MaterialKind.Marble, anchored: true),
            Trophy(new Vector3(2, 15f, -24)),
            new SceneObject
            {
                Name = "Winner", Shape = ShapeKind.Block, Position = new Vector3(2, 14.25f, -26.5f),
                Size = new Vector3(4, 0.5f, 2), Color = new Color4(0.95f, 0.80f, 0.25f, 1f),
                Material = MaterialKind.Neon, Anchored = true, IsCheckpoint = true,
                Script = "won = false\nfunction onTouch(player)\n if not won then\n  won = true\n" +
                    "  game:log(\"You beat Sky Obby in \" .. math.floor(game.time) .. \"s!\")\n" +
                    "  game:playSound(\"stomp\", 1)\n end\nend\n",
            },
        ],
    };
}
