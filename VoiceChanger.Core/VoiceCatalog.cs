using Cat = VoiceChanger.Core.VoiceCategory;
using Shape = VoiceChanger.Core.Dsp.FormantPitchShifter.ModShape;
using Noise = VoiceChanger.Core.Dsp.NoiseLayer.Kind;

namespace VoiceChanger.Core;

/// <summary>Every voice the app offers, in display order. Icon keys match Voxel/Icons.xaml. Recipes: docs/VOICES.md.</summary>
public static class VoiceCatalog
{
    public static readonly IReadOnlyList<Voice> All =
    [
        // ---------- AI (real-time voice conversion: a different person) ----------
        // MeanVC2: one shared model, each voice a 1 KB speaker file (LibriSpeech readers 1919 and 777). ~25% of one core.
        new("Woman", Cat.AI, "woman") { AiModel = "meanvc/woman" },
        new("Man", Cat.AI, "man") { AiModel = "meanvc/man" },
        // LLVC, 52 ms chunks: ~40% CPU even on a laptop on battery; the 26 ms variant (research/) is lighter on delay but ~70%.
        new("Studio voice", Cat.AI, "atom") { AiModel = "llvc-demo-c4" },

        // ---------- Fun ----------
        new("Clown", Cat.Fun, "mood-smile") { Pitch = 1.6f, WobbleHz = 5f, WobbleDepthMs = 1.2f },
        new("Chipmunk", Cat.Fun, "nut") { Pitch = 2.0f },
        new("Helium", Cat.Fun, "balloon") { Pitch = 1.5f },
        new("Wobbly", Cat.Fun, "beer") { WobbleHz = 1f, WobbleDepthMs = 25f },
        new("Twins", Cat.Fun, "users") { WobbleHz = 0.8f, WobbleDepthMs = 3f, WobbleBaseMs = 25f, WobbleMix = 0.5f },
        new("Autotune", Cat.Fun, "adjustments") { Autotune = 1f, ReverbMix = 0.15f },
        new("Squeaky", Cat.Fun, "mouse") { Pitch = 2.2f, Formant = 1.4f },
        new("Slow-mo", Cat.Fun, "hourglass") { Pitch = 0.6f, WobbleHz = 0.4f, WobbleDepthMs = 12f, LowPassHz = 3000f },
        new("Chipmunk choir", Cat.Fun, "users-group") { Pitch = 1.8f, DoublePitch = 1.03f, DoubleMix = 0.6f, WobbleHz = 0.7f, WobbleDepthMs = 4f, WobbleBaseMs = 20f, WobbleMix = 0.5f },
        new("Stutter", Cat.Fun, "repeat") { StutterHz = 8f, StutterDuty = 0.6f },
        new("Nasal", Cat.Fun, "mask") { EqMidHz = 1200f, EqMidDb = 12f, EqLowDb = -8f, Formant = 1.05f },
        new("Mosquito", Cat.Fun, "bug") { Pitch = 2.5f, RingHz = 400f, RingMix = 0.5f, HighPassHz = 1500f },
        new("Kazoo", Cat.Fun, "tornado") { RingHz = 240f, RingMix = 0.6f, Drive = 0.4f, HighPassHz = 300f, LowPassHz = 5000f },
        new("Duck", Cat.Fun, "egg") { Formant = 1.3f, Pitch = 1.1f, EqMidHz = 1500f, EqMidDb = 10f, Drive = 0.15f },
        new("Sleepy", Cat.Fun, "zzz") { Pitch = 0.9f, PitchModShape = Shape.Droop, PitchModHz = 0.5f, PitchModSemitones = 3f, LowPassHz = 2500f, WobbleHz = 0.3f, WobbleDepthMs = 6f },
        new("Hyper", Cat.Fun, "bolt") { Pitch = 1.2f, PitchModShape = Shape.Random, PitchModSemitones = 2f, EqHighDb = 6f, EchoMs = 90f, EchoMix = 0.25f },
        new("Bass boost", Cat.Fun, "volume") { EqLowDb = 14f, Compress = 0.5f, Drive = 0.1f },
        new("Tiny", Cat.Fun, "arrows-minimize") { Pitch = 1.7f, Formant = 1.45f, HighPassHz = 300f },
        new("Huge", Cat.Fun, "arrows-maximize") { Pitch = 0.55f, Formant = 0.65f, ReverbMix = 0.3f },

        // ---------- People ----------
        new("Girl", Cat.People, "mood-kid") { Pitch = 1.5f, Formant = 1.2f },
        new("Boy", Cat.People, "mood-boy") { Pitch = 1.4f, Formant = 1.15f, HighPassHz = 120f },
        new("Deep man", Cat.People, "user") { Pitch = 0.72f, Formant = 0.85f, EqLowDb = 5f, ReverbMix = 0.1f },
        new("Teen", Cat.People, "headphones") { Pitch = 1.15f, Formant = 1.08f, EqMidHz = 2000f, EqMidDb = 4f, PitchModShape = Shape.Random, PitchModSemitones = 1.5f },
        new("Kid", Cat.People, "lego") { Pitch = 1.6f, Formant = 1.3f },
        new("Toddler", Cat.People, "baby-bottle") { Pitch = 1.8f, Formant = 1.35f, Noise = Noise.Hiss, NoiseLevel = 0.03f },
        new("Baby", Cat.People, "baby-carriage") { Pitch = 1.8f, HighPassHz = 200f },
        new("Big guy", Cat.People, "barbell") { Pitch = 0.8f, Formant = 0.88f, Compress = 0.4f },
        new("Grandpa", Cat.People, "eyeglass") { Pitch = 0.9f, TremoloHz = 5.5f, TremoloDepth = 0.35f, LowPassHz = 4000f },
        new("Announcer", Cat.People, "microphone-2") { Pitch = 0.95f, Compress = 0.7f, EqHighDb = 5f, ReverbMix = 0.2f, ReverbSize = 0.5f },
        new("Narrator", Cat.People, "book") { Compress = 0.5f, EqLowDb = 4f, EqMidHz = 300f, EqMidDb = 3f, ReverbMix = 0.08f, ReverbSize = 0.3f },
        new("Radio DJ", Cat.People, "disc") { Compress = 0.8f, EqLowDb = 6f, EqHighDb = 4f, Formant = 0.92f },
        new("Drill sergeant", Cat.People, "military-rank") { Compress = 1f, Drive = 0.35f, EqMidHz = 2500f, EqMidDb = 8f, HighPassHz = 250f, LowPassHz = 5000f },
        new("Hype man", Cat.People, "confetti") { Pitch = 1.05f, Compress = 0.8f, EqHighDb = 6f, EchoMs = 110f, EchoFeedback = 0.2f, EchoMix = 0.35f },

        // ---------- Creatures ----------
        new("Elf", Cat.Creatures, "wand") { Pitch = 1.35f, ReverbMix = 0.15f },
        new("Witch", Cat.Creatures, "cat") { Pitch = 1.25f, Drive = 0.2f, ReverbMix = 0.2f, TremoloHz = 6f, TremoloDepth = 0.2f },
        new("Giant", Cat.Creatures, "mountain") { Pitch = 0.6f, ReverbMix = 0.3f, EchoMs = 120f, EchoFeedback = 0.2f, EchoMix = 0.3f },
        new("Demon", Cat.Creatures, "flame") { Pitch = 0.55f, Drive = 0.5f, RingHz = 40f, RingMix = 0.4f },
        new("Monster", Cat.Creatures, "skull") { Pitch = 0.65f, Drive = 0.7f, LowPassHz = 3000f },
        new("Zombie", Cat.Creatures, "biohazard") { Pitch = 0.7f, Formant = 0.85f, Drive = 0.45f, PitchModShape = Shape.Droop, PitchModHz = 0.7f, PitchModSemitones = 2f, WobbleHz = 2f, WobbleDepthMs = 6f, Noise = Noise.Bubbles, NoiseLevel = 0.15f },
        new("Vampire", Cat.Creatures, "moon-stars") { Pitch = 0.8f, ReverbMix = 0.35f, HighPassHz = 150f },
        new("Alien", Cat.Creatures, "alien") { Pitch = 1.3f, WobbleHz = 6f, WobbleDepthMs = 4f, RingHz = 30f, RingMix = 0.3f },
        new("Werewolf", Cat.Creatures, "moon") { Pitch = 0.7f, Formant = 0.85f, Drive = 0.5f, Noise = Noise.Hiss, NoiseLevel = 0.05f, TremoloHz = 8f, TremoloDepth = 0.25f },
        new("Dragon", Cat.Creatures, "sword") { Pitch = 0.5f, Formant = 0.7f, Drive = 0.5f, ReverbMix = 0.35f, ReverbSize = 0.8f, EqLowDb = 8f },
        new("Orc", Cat.Creatures, "axe") { Pitch = 0.65f, Formant = 0.8f, Drive = 0.6f, EqMidHz = 800f, EqMidDb = 5f },
        new("Troll", Cat.Creatures, "tree") { Pitch = 0.6f, Formant = 0.75f, PitchModShape = Shape.Droop, PitchModHz = 0.4f, PitchModSemitones = 2f },
        new("Goblin", Cat.Creatures, "diamond") { Pitch = 1.35f, Formant = 1.1f, Drive = 0.3f, EqMidHz = 1500f, EqMidDb = 8f },
        new("Ogre", Cat.Creatures, "meat") { Pitch = 0.55f, ReverbMix = 0.3f, LowPassHz = 2500f, TremoloHz = 2f, TremoloDepth = 0.2f },
        new("Dwarf", Cat.Creatures, "hammer") { Pitch = 0.8f, Formant = 0.8f, Compress = 0.5f, EqMidHz = 600f, EqMidDb = 4f },
        new("Fairy", Cat.Creatures, "sparkles") { Pitch = 1.5f, Formant = 1.2f, ReverbMix = 0.4f, ReverbSize = 0.8f, WobbleHz = 0.8f, WobbleDepthMs = 3f, WobbleBaseMs = 15f, WobbleMix = 0.5f },
        new("Siren", Cat.Creatures, "anchor") { Pitch = 1.2f, WobbleHz = 0.5f, WobbleDepthMs = 6f, WobbleBaseMs = 25f, WobbleMix = 0.5f, ReverbMix = 0.5f, ReverbSize = 0.9f, PitchModShape = Shape.Sine, PitchModHz = 4f, PitchModSemitones = 0.4f },
        new("Mummy", Cat.Creatures, "bandage") { Pitch = 0.85f, HighPassHz = 400f, LowPassHz = 3000f, Noise = Noise.Hiss, NoiseLevel = 0.04f, TremoloHz = 1.5f, TremoloDepth = 0.3f },
        new("Skeleton", Cat.Creatures, "bone") { HighPassHz = 700f, RingHz = 90f, RingMix = 0.25f, Bits = 8, TremoloHz = 14f, TremoloDepth = 0.4f },
        new("Swamp thing", Cat.Creatures, "plant") { Pitch = 0.7f, LowPassHz = 900f, WobbleHz = 1.2f, WobbleDepthMs = 8f, Noise = Noise.Bubbles, NoiseLevel = 0.25f },
        new("Snake", Cat.Creatures, "spiral") { Formant = 1.05f, EqHighDb = 10f, Noise = Noise.Hiss, NoiseLevel = 0.06f, WobbleHz = 0.6f, WobbleDepthMs = 2f, WobbleBaseMs = 12f, WobbleMix = 0.4f },
        new("Bear", Cat.Creatures, "trees") { Pitch = 0.6f, Formant = 0.75f, LowPassHz = 2500f, Drive = 0.2f },
        new("Kraken", Cat.Creatures, "fish") { Pitch = 0.5f, LowPassHz = 700f, ReverbMix = 0.4f, ReverbSize = 0.8f, WobbleHz = 0.5f, WobbleDepthMs = 8f },
        new("Golem", Cat.Creatures, "wall") { Pitch = 0.6f, RingHz = 25f, RingMix = 0.5f, Drive = 0.4f, Bits = 10 },
        new("Banshee", Cat.Creatures, "wind") { Pitch = 1.4f, WobbleHz = 0.3f, WobbleDepthMs = 5f, WobbleBaseMs = 2f, WobbleMix = 0.5f, WobbleFeedback = 0.6f, ReverbMix = 0.5f, ReverbSize = 0.9f, TremoloHz = 5f, TremoloDepth = 0.3f },
        new("Imp", Cat.Creatures, "pacman") { Pitch = 1.45f, Formant = 1.1f, Drive = 0.2f, EchoMs = 80f, EchoMix = 0.3f },

        // ---------- Spooky ----------
        new("Ghost", Cat.Spooky, "ghost") { Pitch = 0.85f, ReverbMix = 0.6f, TremoloHz = 3f, TremoloDepth = 0.4f, EchoMs = 300f, EchoFeedback = 0.4f, EchoMix = 0.4f },
        new("Possessed", Cat.Spooky, "eye") { DoublePitch = 0.6f, DoubleFormant = 0.8f, DoubleMix = 0.7f, DoubleDrive = 0.5f, ReverbMix = 0.3f },
        new("Poltergeist", Cat.Spooky, "arrows-shuffle") { WobbleHz = 0.4f, WobbleDepthMs = 8f, WobbleBaseMs = 1f, WobbleMix = 0.5f, WobbleFeedback = 0.7f, ReverbMix = 0.4f, TremoloHz = 9f, TremoloDepth = 0.5f },
        new("Haunted doll", Cat.Spooky, "horse-toy") { Pitch = 1.3f, Formant = 1.2f, Bits = 7, Noise = Noise.Crackle, NoiseLevel = 0.3f, PitchModShape = Shape.Droop, PitchModHz = 0.3f, PitchModSemitones = 2f },
        new("Creepy whisper", Cat.Spooky, "ear") { VocoderMix = 1f, VocoderNoise = true, HighPassHz = 500f, ReverbMix = 0.2f, ReverbSize = 0.3f, Gain = 1.4f },
        new("Shadow", Cat.Spooky, "shadow") { Pitch = 0.9f, HighPassHz = 300f, LowPassHz = 2500f, ReverbMix = 0.7f, ReverbSize = 0.9f, TremoloHz = 0.8f, TremoloDepth = 0.3f },
        new("Cursed", Cat.Spooky, "candle") { Pitch = 0.85f, RingHz = 60f, RingMix = 0.35f, ReverbMix = 0.4f, WobbleHz = 0.2f, WobbleDepthMs = 4f, WobbleBaseMs = 1f, WobbleMix = 0.5f, WobbleFeedback = 0.5f },
        new("Séance", Cat.Spooky, "crystal-ball") { HighPassHz = 600f, LowPassHz = 2500f, Noise = Noise.Static, NoiseLevel = 0.08f, ReverbMix = 0.4f, TremoloHz = 0.5f, TremoloDepth = 0.4f },
        new("Nightmare", Cat.Spooky, "bed") { Pitch = 0.75f, WobbleHz = 0.3f, WobbleDepthMs = 15f, ReverbMix = 0.6f, ReverbSize = 1f, LowPassHz = 2000f },

        // ---------- Machines ----------
        new("Robot", Cat.Machines, "robot") { RingHz = 70f, RingMix = 0.9f, Drive = 0.1f },
        new("Android", Cat.Machines, "cpu") { Pitch = 1.15f, RingHz = 120f, RingMix = 0.8f },
        new("Cyborg", Cat.Machines, "brain") { RingHz = 55f, RingMix = 0.5f, Drive = 0.1f, Formant = 0.95f },
        new("AI assistant", Cat.Machines, "message-chatbot") { VocoderMix = 0.35f, Autotune = 0.4f, EqHighDb = 4f },
        new("Computer voice", Cat.Machines, "device-desktop") { VocoderMix = 0.9f, Autotune = 1f, HighPassHz = 200f, LowPassHz = 5000f },
        new("Evil machine", Cat.Machines, "alert-octagon") { RingHz = 30f, RingMix = 0.7f, Drive = 0.7f, HighPassHz = 300f, LowPassHz = 4000f, Compress = 0.7f },
        new("Glitch", Cat.Machines, "terminal-2") { StutterHz = 12f, StutterDuty = 0.7f, StutterRandom = true, Bits = 6, PitchModShape = Shape.Random, PitchModSemitones = 5f },
        new("Game console", Cat.Machines, "device-gamepad-2") { Bits = 6, RateDivider = 6, HighPassHz = 200f, LowPassHz = 3500f },
        new("Dial-up modem", Cat.Machines, "router") { RingHz = 1200f, RingMix = 0.6f, Bits = 6, Noise = Noise.Static, NoiseLevel = 0.1f, HighPassHz = 500f, LowPassHz = 4000f },
        new("Vocoder", Cat.Machines, "wave-square") { VocoderMix = 1f, WobbleHz = 0.6f, WobbleDepthMs = 3f, WobbleBaseMs = 18f, WobbleMix = 0.4f },
        new("Synth voice", Cat.Machines, "piano") { VocoderMix = 0.8f, Autotune = 0.8f, ReverbMix = 0.3f },
        new("Mech", Cat.Machines, "engine") { Pitch = 0.6f, RingHz = 40f, RingMix = 0.5f, Drive = 0.4f, ReverbMix = 0.3f, ReverbSize = 0.5f },
        new("Drone", Cat.Machines, "drone") { RingHz = 180f, RingMix = 0.7f, WobbleHz = 6f, WobbleDepthMs = 2f, WobbleBaseMs = 1f, WobbleMix = 0.5f, WobbleFeedback = 0.6f, HighPassHz = 300f, LowPassHz = 5000f },
        new("Satellite", Cat.Machines, "satellite") { EchoMs = 600f, EchoMix = 0.5f, HighPassHz = 300f, LowPassHz = 3500f, StutterHz = 3f, StutterDuty = 0.85f, StutterRandom = true, Noise = Noise.Static, NoiseLevel = 0.05f },
        new("Toy robot", Cat.Machines, "robot-face") { Pitch = 1.2f, RingHz = 150f, RingMix = 0.6f, Bits = 7, HighPassHz = 300f, LowPassHz = 4000f },
        new("Fax", Cat.Machines, "printer") { RingHz = 2000f, RingMix = 0.5f, Drive = 0.4f, HighPassHz = 800f, LowPassHz = 3000f, Noise = Noise.Static, NoiseLevel = 0.08f },

        // ---------- Places ----------
        new("Echo", Cat.Places, "wave-sine") { EchoMs = 350f, EchoFeedback = 0.45f, EchoMix = 0.5f },
        new("Stadium", Cat.Places, "building-stadium") { ReverbMix = 0.5f, EchoMs = 200f, EchoFeedback = 0.3f, EchoMix = 0.35f },
        new("Underwater", Cat.Places, "ripple") { LowPassHz = 600f, WobbleHz = 1.5f, WobbleDepthMs = 3f, WobbleMix = 0.6f },
        new("Cathedral", Cat.Places, "building-church") { ReverbMix = 0.7f, ReverbSize = 1f, HighPassHz = 120f },
        new("Tunnel", Cat.Places, "building-tunnel") { EchoMs = 180f, EchoFeedback = 0.6f, EchoMix = 0.5f, ReverbMix = 0.3f, LowPassHz = 4000f },
        new("Canyon", Cat.Places, "cactus") { EchoMs = 500f, EchoFeedback = 0.5f, EchoMix = 0.5f, ReverbMix = 0.3f, ReverbSize = 0.9f, HighPassHz = 200f },
        new("Space station", Cat.Places, "rocket") { Noise = Noise.Hum, NoiseLevel = 0.06f, HighPassHz = 200f, LowPassHz = 6000f, ReverbMix = 0.1f, ReverbSize = 0.2f },
        new("Bathroom", Cat.Places, "bath") { ReverbMix = 0.5f, ReverbSize = 0.3f, EqHighDb = 5f },
        new("Phone booth", Cat.Places, "device-landline-phone") { HighPassHz = 250f, LowPassHz = 4000f, ReverbMix = 0.3f, ReverbSize = 0.2f, Compress = 0.4f },
        new("Elevator", Cat.Places, "elevator") { ReverbMix = 0.35f, ReverbSize = 0.25f, LowPassHz = 3000f, Noise = Noise.Hum, NoiseLevel = 0.04f },
        new("Submarine", Cat.Places, "submarine") { ReverbMix = 0.4f, ReverbSize = 0.5f, LowPassHz = 2500f, EchoMs = 900f, EchoMix = 0.2f, RingHz = 0f },
        new("Bunker", Cat.Places, "shield") { LowPassHz = 2000f, ReverbMix = 0.3f, ReverbSize = 0.35f },
        new("Parking garage", Cat.Places, "parking") { EchoMs = 250f, EchoFeedback = 0.4f, EchoMix = 0.4f, ReverbMix = 0.4f, EqMidHz = 800f, EqMidDb = 3f },
        new("Concert hall", Cat.Places, "music") { ReverbMix = 0.6f, ReverbSize = 0.85f, WobbleHz = 0.5f, WobbleDepthMs = 2f, WobbleBaseMs = 15f, WobbleMix = 0.3f },
        new("Mountain top", Cat.Places, "flag") { Noise = Noise.Wind, NoiseLevel = 0.3f, EchoMs = 700f, EchoFeedback = 0.35f, EchoMix = 0.4f, HighPassHz = 200f },

        // ---------- Devices ----------
        new("Telephone", Cat.Devices, "phone") { HighPassHz = 300f, LowPassHz = 3400f, Drive = 0.15f },
        new("Radio", Cat.Devices, "radio") { HighPassHz = 400f, LowPassHz = 3000f, Drive = 0.35f },
        new("Walkie-talkie", Cat.Devices, "antenna") { HighPassHz = 500f, LowPassHz = 2500f, Drive = 0.5f },
        new("Megaphone", Cat.Devices, "speakerphone") { HighPassHz = 250f, LowPassHz = 5000f, Drive = 0.6f, Gain = 1.2f },
        new("Trash mic", Cat.Devices, "trash") { HighPassHz = 400f, LowPassHz = 3000f, Drive = 0.4f, Bits = 7, Noise = Noise.Static, NoiseLevel = 0.08f },
        new("Drive-thru", Cat.Devices, "burger") { HighPassHz = 500f, LowPassHz = 2800f, Drive = 0.5f, Noise = Noise.Static, NoiseLevel = 0.06f, Compress = 0.6f },
        new("Airport PA", Cat.Devices, "plane") { HighPassHz = 300f, LowPassHz = 4000f, ReverbMix = 0.5f, ReverbSize = 0.9f, EchoMs = 220f, EchoMix = 0.3f, Compress = 0.5f },
        new("Old vinyl", Cat.Devices, "vinyl") { LowPassHz = 5000f, Noise = Noise.Crackle, NoiseLevel = 0.4f, WobbleHz = 0.55f, WobbleDepthMs = 1.5f },
        new("Cassette", Cat.Devices, "device-tv-old") { Noise = Noise.Hiss, NoiseLevel = 0.08f, WobbleHz = 5f, WobbleDepthMs = 0.6f, LowPassHz = 8000f },
        new("Baby monitor", Cat.Devices, "wifi") { HighPassHz = 500f, LowPassHz = 3000f, Noise = Noise.Static, NoiseLevel = 0.12f, Gain = 0.7f, TremoloHz = 0.3f, TremoloDepth = 0.2f },
        new("Intercom", Cat.Devices, "bell") { HighPassHz = 350f, LowPassHz = 3500f, Drive = 0.3f, Noise = Noise.Hum, NoiseLevel = 0.05f },
        new("CB radio", Cat.Devices, "truck") { HighPassHz = 400f, LowPassHz = 2800f, Drive = 0.45f, Noise = Noise.Static, NoiseLevel = 0.1f, StutterHz = 1f, StutterDuty = 0.95f, StutterRandom = true },
        new("Cheap headset", Cat.Devices, "headset") { HighPassHz = 600f, Compress = 0.5f, EqHighDb = 3f },
        new("Vintage mic", Cat.Devices, "microphone") { HighPassHz = 200f, LowPassHz = 5000f, Drive = 0.15f, Compress = 0.6f },
        new("Broken speaker", Cat.Devices, "device-speaker") { Drive = 0.9f, RingHz = 50f, RingMix = 0.2f, HighPassHz = 300f, LowPassHz = 4000f },
        new("Voicemail", Cat.Devices, "mail") { HighPassHz = 300f, LowPassHz = 3400f, Bits = 8, RateDivider = 3 },

        // ---------- Animals ----------
        new("Cat", Cat.Animals, "paw") { Pitch = 1.4f, Formant = 1.25f, EqMidHz = 1800f, EqMidDb = 8f, PitchModShape = Shape.Sine, PitchModHz = 6f, PitchModSemitones = 0.6f },
        new("Bee", Cat.Animals, "hexagon") { RingHz = 220f, RingMix = 0.7f, HighPassHz = 300f, LowPassHz = 4000f, TremoloHz = 25f, TremoloDepth = 0.3f },
        new("Frog", Cat.Animals, "leaf") { Pitch = 0.7f, RingHz = 35f, RingMix = 0.5f, TremoloHz = 12f, TremoloDepth = 0.5f, LowPassHz = 2500f },
        new("Bird", Cat.Animals, "feather") { Pitch = 2.0f, Formant = 1.4f, HighPassHz = 1200f, PitchModShape = Shape.Sine, PitchModHz = 9f, PitchModSemitones = 1f },
        new("Lion", Cat.Animals, "crown") { Pitch = 0.55f, Formant = 0.7f, Drive = 0.5f, ReverbMix = 0.3f },
        new("Monkey", Cat.Animals, "mood-crazy-happy") { Pitch = 1.5f, Formant = 1.2f, Drive = 0.2f, PitchModShape = Shape.Random, PitchModSemitones = 4f },
        new("Pig", Cat.Animals, "pig") { Pitch = 1.1f, Formant = 1.15f, EqMidHz = 1300f, EqMidDb = 9f, Drive = 0.3f },
        new("Goat", Cat.Animals, "cheese") { Pitch = 1.2f, Formant = 1.1f, TremoloHz = 7f, TremoloDepth = 0.7f },
    ];
}
