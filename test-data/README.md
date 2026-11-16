# Test data

Sample media for trying and evaluating Keyframe. [TESTING.md](../TESTING.md) has cURL scripts that use these files. Every file is small enough to send inline as base64.

## Images

| File | Content | Good fields to try |
|---|---|---|
| `images/cat.jpg` | An orange tabby cat with green eyes looking at the camera, red and white stripes blurred behind it | `animal` (Enum: cat, dog, other), `eyeColor` (String) |
| `images/dog.jpg` | A yellow Labrador retriever standing on grass, tail raised | `breed` (String), `isOutdoors` (Boolean) |
| `images/eiffel-tower.jpg` | The Eiffel Tower seen from the Champ de Mars under a clear blue sky, portrait orientation | `landmark` (String), `city` (String) |
| `images/food.jpg` | A table of food: fish, poultry, bread, peppers, carrots, squash, oranges, grains in jars | `foods` (StringList), `containsMeat` (Boolean) |
| `images/receipt.jpg` | A Swiss restaurant receipt from Berghotel Grosse Scheidegg, dated 30.07.2007, total CHF 54.50 | `merchant` (String), `total` (Number), `currency` (String), `date` (String) |
| `images/test-pattern.png` | A synthetic ffmpeg `testsrc2` pattern | `hasPeople` should be false; a sanity check for hallucination |

## Audio

Speech was generated with macOS text to speech (`say`), so the exact words are known (see the scripts below).

| File | Format | Length | Content |
|---|---|---|---|
| `audio/meeting-two-speakers.mp3` | MP3, 22 kHz mono | 46 s | Two-speaker status meeting for the Atlas project (Sarah and Daniel): search latency fell from 400 ms to about 120 ms; billing migration blocked on finance approval, about two weeks behind; action items: Sarah emails the finance director today, Daniel prepares a rollback plan by Thursday; next meeting Tuesday at 10 a.m. |
| `audio/voicemail.m4a` | AAC, 16 kHz mono | 17 s | Karen from Bayside Dental confirms Michael Chen's cleaning on Thursday, June 12 at 2:30 p.m.; callback number 555-214-8839 |
| `audio/podcast-intro.wav` | WAV PCM, 16 kHz mono | 17 s | Intro of a nature podcast, "Field Notes": puffins on the Norwegian coast, falling numbers, volunteers |
| `audio/spanish-hotel-reservation.flac` | FLAC, 16 kHz mono | 14 s | Spanish: Hotel Mirador confirms a two-night stay, August 15 to 17, double room with sea view, breakfast included. Tests language detection (`es`) |
| `audio/tone-and-silence-no-speech.ogg` | Opus, 16 kHz mono | 10 s | A 440 Hz tone, then silence. There is no speech; the transcript should be empty or near empty and fields should come back null |

## Video

| File | Length | Content |
|---|---|---|
| `video/narrated-slideshow.mp4` | 21 s, 1280x720, AAC narration | Four slides with spoken captions: the cat, the dog, the Eiffel Tower, and the food table. Expect 4 scenes whose captions match the narration |
| `video/big-buck-bunny-45s.mp4` | 45 s, 640x360 | From Big Buck Bunny (1:00 to 1:45): a large grey rabbit wakes, smells a flower, a pink butterfly, the rabbit in a meadow holding a red apple. Music and effects, no dialogue |
| `video/elephants-dream-dialogue-60s.mp4` | 60 s, 640x360 | From Elephants Dream (0:15 to 1:15): the title card, then a pale mechanical world of cables and machines where an older bald man (Proog) and a younger man (Emo) meet. Dialogue from about 0:01 to 0:50 of the clip, including "Emo?", "Are you hurt?", "I don't think so. You?", and "Emo, it's not safe here." |
| `video/scene-cuts-12s-silent.mp4` | 12 s, 640x360, no audio | Three hard cuts at 4 s and 8 s: a test pattern, SMPTE color bars, then solid navy. Scene detection should return three scenes; transcription is skipped with a warning |

## Sources and licenses

| File | Source | License |
|---|---|---|
| `images/cat.jpg` | [Cat03.jpg](https://commons.wikimedia.org/wiki/File:Cat03.jpg) by Fir0002 | GFDL 1.2 |
| `images/dog.jpg` | [YellowLabradorLooking_new.jpg](https://commons.wikimedia.org/wiki/File:YellowLabradorLooking_new.jpg), derivative work by Djmirko | CC BY-SA 3.0 |
| `images/eiffel-tower.jpg` | [Tour_Eiffel_Wikimedia_Commons.jpg](https://commons.wikimedia.org/wiki/File:Tour_Eiffel_Wikimedia_Commons.jpg) by Benh LIEU SONG (960 px thumbnail) | Public domain |
| `images/food.jpg` | [Good_Food_Display_-_NCI_Visuals_Online.jpg](https://commons.wikimedia.org/wiki/File:Good_Food_Display_-_NCI_Visuals_Online.jpg), National Cancer Institute (960 px thumbnail) | Public domain |
| `images/receipt.jpg` | [ReceiptSwiss.jpg](https://commons.wikimedia.org/wiki/File:ReceiptSwiss.jpg) by Audrius Meskauskas (960 px thumbnail) | CC BY-SA 3.0 |
| `video/big-buck-bunny-45s.mp4` | [Big Buck Bunny](https://commons.wikimedia.org/wiki/File:Big_Buck_Bunny_4K.webm), (c) Blender Foundation, peach.blender.org; trimmed and re-encoded | CC BY 3.0 |
| `video/elephants-dream-dialogue-60s.mp4` | [Elephants Dream](https://commons.wikimedia.org/wiki/File:Elephants_Dream_(2006).webm), Orange Open Movie Team; trimmed and re-encoded | CC BY 2.5 |
| `video/narrated-slideshow.mp4` | Built from the images above with macOS `say` narration | Inherits the image licenses |
| `images/test-pattern.png`, `video/scene-cuts-12s-silent.mp4`, all of `audio/` | Generated for this repository with ffmpeg and macOS `say` | MIT, like the repository |

## Regenerating

The audio files and the slideshow were made like this (macOS, ffmpeg 7):

```bash
say -v Karen -o vm.aiff "Hi, this is Karen from Bayside Dental calling for Michael Chen. ..."
ffmpeg -i vm.aiff -ar 16000 -ac 1 -c:a aac -b:a 48k voicemail.m4a

ffmpeg -f lavfi -i "sine=frequency=440:duration=4:sample_rate=16000" -f lavfi -i "anullsrc=r=16000:cl=mono:d=6" \
  -filter_complex "[0:a][1:a]concat=n=2:v=0:a=1" -c:a libopus -b:a 32k tone-and-silence-no-speech.ogg
```

The meeting uses the voices Samantha (Sarah) and Daniel, the voicemail Karen, the podcast Moira, the Spanish clip Paulina, and the slideshow Samantha. Each slide is the image letterboxed to 1280x720 and held for the narration length plus one second.
