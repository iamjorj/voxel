# Voice plan — 100 new voices (125 total)

Draft for approval. Each voice is a *layered* recipe built from the engine stages. "Sound" says what the
listener should hear; "Recipe" is the shorthand for how it's made.

## Engine upgrades needed first

Background sounds (static, hiss, crackle, hum, wind, bubbles) were removed on 2026-11-02 at the user's request: every voice is the voice effect only.

| Stage | Why |
|---|---|
| Formant shift (independent of pitch) | Believable girl/boy/woman/man voices instead of chipmunk/giant |
| Vocoder (carrier: saw / noise) | Synth voices, whispers, Daleks, insects |
| Autotune (pitch detect → snap to scale) | Autotune, singer, robot-singer voices |
| Bit-crusher / sample-rate reducer | Game console, dial-up, glitch, cheap devices |
| Flanger (modulated delay with feedback) | Jet-engine swoosh, alien, poltergeist |
| 3-band EQ | Every device voice, nasal/bass voices |
| Pitch envelope (droop / rise, non-sine LFO) | Zombie droop, sleepy, drunk slides |
| Gate / stutter | Stutter, glitch, walkie-talkie squelch |
| Compressor | Shouting voices (drill sergeant, hype man, megaphone) |

## People (12)

The realistic Man, E-girl and Grandpa are AI voices (MeanVC2, category AI); the old pitch-shift Woman, Man and Grandpa were removed, and the AI Woman was replaced by E-girl.

| Voice | Sound | Recipe |
|---|---|---|
| Girl | Natural higher female voice | pitch 1.25, formant +25% |
| Boy | Young boy | pitch 1.3, formant +15%, slight high-pass |
| Deep man | Movie-trailer baritone | pitch 0.8, formant −20%, bass EQ, light reverb |
| Teen | Cracking, slightly nasal | pitch 1.1, formant +8%, mid EQ, random pitch flicks |
| Kid | Small child | pitch 1.45, formant +30% |
| Toddler | Tiny, breathy | pitch 1.6, formant +35% |
| Big guy | Large, chesty | pitch 0.85, formant −15%, compressor |
| Announcer | Stadium/boxing announcer | compressor, bright EQ, short reverb, slight pitch 0.95 |
| Narrator | Close, warm documentary voice | compressor, low-mid EQ boost, tiny room |
| Radio DJ | Smooth, bassy, compressed | compressor, bass + presence EQ, formant −8% |
| Drill sergeant | Shouting, cutting | compressor hard, distortion, high-mid EQ, megaphone band |
| Hype man | Loud, excited, echoey | compressor, bright EQ, slap-echo, pitch 1.05 |

## Creatures (18)

| Voice | Sound | Recipe |
|---|---|---|
| Werewolf | Growl with breathy edge | pitch 0.7, drive, formant −15%, tremolo 8 Hz |
| Dragon | Huge, fiery, rumbling | pitch 0.5, drive, reverb, low rumble layer |
| Orc | Guttural, rough | pitch 0.65, drive 0.6, formant −20%, mid EQ |
| Troll | Dumb, slow, low | pitch 0.6, formant −25%, slow pitch droop |
| Goblin | Sneaky, high, raspy | pitch 1.35, drive 0.3, formant +10%, nasal EQ |
| Ogre | Boomy and wet | pitch 0.55, reverb, low-pass, slow tremolo |
| Dwarf | Gruff, mid-low, tight | pitch 0.8, formant −20%, compressor, mid EQ |
| Fairy | Light, sparkly, airy | pitch 1.5, formant +20%, shimmer reverb, chorus |
| Siren | Hypnotic, detuned, wet | chorus wide, reverb long, pitch 1.2, slow vibrato |
| Mummy | Dry, hollow, ancient | pitch 0.85, band-pass, slow tremolo |
| Skeleton | Rattly, thin, clicky | high-pass, ring 90 Hz light, bit-crush light, tremolo fast |
| Swamp thing | Gurgly, muddy | pitch 0.7, low-pass 900, wobble |
| Snake | Hissy, sibilant, sly | formant +5%, high EQ boost, slight chorus |
| Bear | Deep huff | pitch 0.6, formant −25%, low-pass 2.5k, drive light |
| Kraken | Deep underwater monster | pitch 0.5, low-pass 700, reverb, wobble slow |
| Golem | Stone grinding | pitch 0.6, ring 25 Hz, drive, bit-crush light |
| Banshee | Wailing, shrill, haunted | pitch 1.4, flanger, reverb long, tremolo |
| Imp | Fast, mischievous | pitch 1.45, drive light, slap-echo, formant +10% |

## Spooky (8)

| Voice | Sound | Recipe |
|---|---|---|
| Possessed | Two voices, one evil | layer: dry + pitch 0.6 drive copy, reverb |
| Poltergeist | Whooshing, unstable | flanger deep, wobble, reverb, tremolo random |
| Haunted doll | Sweet but wrong | pitch 1.3, formant +20%, bit-crush, vinyl crackle, slow droop |
| Creepy whisper | Breathy, close, no tone | noise-vocoder, high-pass, close reverb |
| Shadow | Hollow and distant | pitch 0.9, band-pass, long reverb, low volume tremolo |
| Cursed | Reversed-feeling, metallic | ring 60 Hz, reverb pre-delay, pitch 0.85, flanger slow |
| Séance | Old radio from beyond | band-pass narrow, reverb, tremolo slow |
| Nightmare | Slowed and stretched | pitch 0.75, wobble slow deep, reverb huge, low-pass |

## Machines (14)

| Voice | Sound | Recipe |
|---|---|---|
| Cyborg | Half human, half ring-mod | ring 55 Hz mix 0.5, drive light, formant −5% |
| AI assistant | Clean, slightly synthetic | vocoder subtle, autotune light, bright EQ |
| Computer voice | 80s text-to-speech | vocoder saw, autotune hard, band-pass |
| Evil machine | Harsh, metallic dictator | ring 30 Hz, drive 0.7, band-pass, compressor |
| Glitch | Stuttery, bit-broken | stutter gate random, bit-crush, pitch flicks |
| Game console | 8-bit handheld | bit-crush 6-bit, sample-rate 8 kHz, band-pass |
| Dial-up modem | Screechy data noise | ring 1.2 kHz, bit-crush, band-pass |
| Vocoder | Classic synth talk-box | vocoder saw full, chorus |
| Synth voice | Smooth, musical | vocoder + autotune, reverb |
| Mech | Giant walking robot | pitch 0.6, ring 40 Hz, drive, reverb metallic |
| Drone | Buzzing flying machine | ring 180 Hz, flanger fast, band-pass |
| Satellite | Delayed, broken link | echo 600 ms single, band-pass, stutter light, static |
| Toy robot | Cute, cheap, plastic | pitch 1.2, ring 150 Hz, bit-crush, band-pass |
| Fax | Shrill, scratchy transmission | ring 2 kHz, drive, band-pass narrow, static |

## Places (12)

| Voice | Sound | Recipe |
|---|---|---|
| Cathedral | Huge holy reverb | reverb very long, high-pass light |
| Tunnel | Long metallic echoes | echo 180 ms feedback high, reverb, low-pass |
| Canyon | Far, slow echoes | echo 500 ms ×3, reverb, high-pass |
| Space station | Dry, close, hum | hum 60 Hz layer, band-pass, tiny reverb |
| Bathroom | Tight tiled room | reverb short bright, high EQ |
| Phone booth | Boxy, enclosed | band-pass, tiny reverb, compressor |
| Elevator | Small metal box, muffled | reverb short, low-pass 3k, hum |
| Submarine | Metal, pressure, pings | reverb metallic, low-pass, ping echo |
| Bunker | Concrete, dead, close | low-pass, reverb short dark |
| Parking garage | Concrete echoes | echo 250 ms, reverb, mid EQ |
| Concert hall | Grand, lush | reverb long warm, slight chorus |
| Mountain top | Wind, far echo | echo 700 ms, high-pass |

## Devices (12)

| Voice | Sound | Recipe |
|---|---|---|
| Trash mic | Awful gamer headset | band-pass 400–3k, drive, bit-crush, static |
| Drive-thru | Crackly speaker box | band-pass, drive, static, compressor |
| Airport PA | Echoey announcement | band-pass, reverb long, echo, compressor |
| Old vinyl | Warm, crackling record | low-pass, slow wobble, mono |
| Cassette | Hissy tape with flutter | wobble fast light, low-pass 8k |
| Baby monitor | Thin, hissy, distant | band-pass, static, low volume, tremolo |
| Intercom | Buzzy office speaker | band-pass, drive, hum |
| CB radio | Trucker radio with squelch | band-pass, drive, static, gate squelch |
| Cheap headset | Tinny, close | high-pass 600, compressor |
| Vintage mic | 1940s broadcast | band-pass 200–5k, drive light, compressor |
| Broken speaker | Rattling, distorted | drive hard, ring 50 Hz light, band-pass |
| Voicemail | Compressed phone message | band-pass, bit-crush, beep intro |

## Fun (14)

| Voice | Sound | Recipe |
|---|---|---|
| Autotune | Pop-star pitch snapping | autotune hard, light reverb |
| Squeaky | Mouse-like | pitch 2.2, formant +40% |
| Slow-mo | Dragged down, thick | pitch 0.6, wobble slow, low-pass |
| Chipmunk choir | Three chipmunks at once | chorus wide ×3, pitch 1.8 |
| Stutter | Rhythmic cut-ups | stutter gate 1/8 |
| Nasal | Blocked nose | band EQ 1 kHz boost, formant +5% |
| Mosquito | Tiny buzzing | pitch 2.5, ring 400 Hz, band-pass high |
| Kazoo | Buzzy party horn | ring 240 Hz, drive, band-pass |
| Duck | Quacky | formant +30%, nasal EQ, pitch 1.1, drive light |
| Sleepy | Slow, slurred, droopy | pitch 0.9, droop envelope, low-pass, slow wobble |
| Hyper | Fast, bright, jittery | pitch 1.2, pitch flicks, bright EQ, slap-echo |
| Bass boost | Earth-shaking lows | bass EQ heavy, compressor, drive light |
| Tiny | Very small person | pitch 1.7, formant +45%, high-pass |
| Huge | Very large person | pitch 0.55, formant −35%, reverb |

## Animals (8)

| Voice | Sound | Recipe |
|---|---|---|
| Cat | Meowy, nasal, high | pitch 1.4, formant +25%, nasal EQ, vibrato |
| Bee | Buzzy insect | ring 220 Hz, band-pass, tremolo fast |
| Frog | Croaky, low, wet | pitch 0.7, ring 35 Hz, tremolo 12 Hz, low-pass |
| Bird | Chirpy, whistly | pitch 2.0, formant +40%, band-pass high, vibrato fast |
| Lion | Roar-like growl | pitch 0.55, drive, formant −30%, reverb |
| Monkey | Screechy, excited | pitch 1.5, formant +20%, drive light, pitch flicks |
| Pig | Snorty, nasal | pitch 1.1, nasal EQ, drive, formant +15% |
| Goat | Bleating vibrato | pitch 1.2, tremolo 7 Hz deep, formant +10% |
