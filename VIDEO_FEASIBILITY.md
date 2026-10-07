# Video backlog — feasibility result (built-in Windows decoder)

Date: 2026-10-07. Machine: Windows 11 (10.0.26200), development PC. Harness: `tools\VideoSpike` (throwaway, not part of the solution, not shipped). Test clips were generated with ffmpeg (synthetic test patterns, not anyone's footage).

**Question:** can the app play local MP4/H.264 clips sized for a Screen (168x672) using only the decoder that ships with Windows (WPF `MediaPlayer`, Media Foundation), grab each frame into the same pixel-buffer pipeline the menus use, and do it smoothly, at 1:1, and failing safely?

**Answer: yes, with one correctable color-range issue.** No third-party decoder is needed for H.264 MP4.

## Results

| Check | Result |
|---|---|
| Opens 168x672, 336x672, 1080p H.264 MP4 | Yes, in 100-530 ms |
| Reports native size and duration | Yes (168x672, 10.0 s, etc.) |
| Smoothness at 30 fps source | Every one of 150 frames in 5 s was a distinct picture (30.0/s); no dropped frames |
| Smoothness at 60 fps source | 60.1 distinct pictures/s at native size, 55.7/s when composed into a 1920x1080 canvas |
| Cost to grab a 168x672 frame | median 1.9 ms (max 4.8); composing it inside a full 1920x1080 canvas median 6 ms (max 31) |
| CPU, one 168x672 video | about 20% of one core grabbing every frame at native size, about 40% when the whole 1080p canvas is redrawn every frame (so the real design should redraw only the video's rectangle) |
| 1080p source grabbed at full size | about 24 fps and 75% of a core, so large clips should be authored to the screen size, as intended |
| Geometry (VID-012) | Same frame, same pixels, no scaling or shift: a captured 168x672 frame lined up with ffmpeg's own decode of that frame (side-by-side image inspected) |
| End of clip, loop | `MediaEnded` fires reliably; restart from zero takes about 300 ms (a visible hitch unless the next clip is prepared early) |
| Audio | Detected (`HasAudio`); the player can be muted |
| HEVC / H.265 | Played here, but only because this PC has the Windows HEVC codec; a clean event PC may not. Treat as unsupported unless import test-opens it |
| Truncated or garbage MP4 | `MediaFailed` within 70-350 ms, no crash, no hang (the error text is misleading: "Cannot find the media file"; the app must show its own message) |

## The one problem: video levels

Windows decodes limited-range video (the normal case for H.264 MP4) correctly except that it does **not** stretch the 16-235 "video range" to 0-255 in what we grab. A properly tagged Rec.709 clip showing pure red arrives as (233,16,15) instead of (255,0,0): whites 235 not 255, blacks 16 not 0. On an LED wall that means slightly gray whites and lifted blacks.

A plain levels expansion `(v-16)*255/219` applied to the grabbed pixels brings the picture to within a mean of 4/255 of ffmpeg's decode (the remainder is edge chroma from 4:2:0 upsampling, decoder dependent). Untagged clips are assumed Rec.709 by Windows (ffmpeg assumes 601), so exports should be tagged Rec.709, which every normal video editor does.

## What this does to the plan

- Decoder decision: **built-in Windows media (WPF MediaPlayer)**. Recommended officially supported format: H.264 MP4 (AAC audio optional). Other formats best effort, test-opened at import.
- Frames are grabbed only for the video Screen's own rectangle and copied into the shared frame, so cost does not grow with canvas size.
- Levels expansion is part of the pipeline.
- Looping needs an early restart or a second player to avoid the 300 ms gap.

## Not tested here

Long soak (hours), memory growth, many videos in sequence, behavior with the real VP2 wall, HEVC on a machine without the codec, and videos with unusual dimensions or variable frame rate.

## Addendum: the user's real clips (2026-10-07)

Three clips from the user's downloads, copied to a temp folder for the test (originals untouched): "Fall Drinks", "Black and Orange Elegant Whiskey Bar Event Promotional Ad", and "Fun Gold Friday Beer Periodic Element Pun".

| | Fall Drinks | Whiskey Bar Ad | Gold Friday Beer |
|---|---|---|---|
| Format (ffprobe) | H.264 High, 168x672, 30 fps, yuv420p, tagged BT.709 limited range, no audio | same | same |
| Real length | 38.5 s (1155 frames) | 6.03 s | 12.93 s (388 frames) |
| Windows reports | 38.0 s | 6.0 s | 12.0 s |
| Opens | 335 ms | 352 ms | 349 ms |
| Smoothness | 30/s frames delivered; only 144 of its 1155 frames differ from the one before, so it is a low-motion clip (the 13.6 "distinct pictures per second" the harness counted is the clip, not the decoder) | 30.0/s, all distinct | 30.0/s, all distinct |
| Grab cost at 168x672 | median 2.4 ms | 2.5 ms | 2.1 ms |
| CPU (grabbing every frame) | 23% of a core | 26% | 18% |

- **Colour on real content:** the Whiskey Bar clip compared with ffmpeg's decode of the same frame: mean difference 11.1/255 as Windows delivers it, **1.2/255 after the levels expansion** (1.7% of channel values differ by more than 6, all at colour edges). The expansion is confirmed on real material.
- **Duration finding:** Windows reports lengths rounded down to whole seconds (12.93 s shows as 12.0 s). Playback itself runs to the true end (MediaEnded fired ~1 s later than the reported length would imply). So: never use the reported length for timing or for "last second" logic; use the end-of-media event. For display, the import should read the real length from the MP4 header itself (a small reader), or show whole seconds.
- Loop restart is still about 0.3 s, as before.
