"""
Generates the placeholder sound effects in Assets/Audio/SFX as 16-bit mono WAVs.

These are synthesized from scratch (noise bursts, tone sweeps, square blips) so the game
has audio without shipping third-party assets. They are deliberately retro/8-bit flavoured.
To swap in real audio later, just replace the .wav files — the filenames are the contract
AudioManager loads by, so no code changes are needed.

Run:  python Tools/generate_sfx.py
"""

import math
import os
import random
import struct
import wave

SR = 22050
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Audio", "SFX")

random.seed(1234)  # deterministic output, so regenerating doesn't churn the files


# ── Building blocks ──────────────────────────────────────────────────────────

def n_samples(seconds):
    return int(SR * seconds)


def noise(n):
    return [random.uniform(-1.0, 1.0) for _ in range(n)]


def sine(n, f0, f1=None):
    f1 = f0 if f1 is None else f1
    out, phase = [], 0.0
    for i in range(n):
        f = f0 + (f1 - f0) * (i / max(n - 1, 1))
        phase += 2.0 * math.pi * f / SR
        out.append(math.sin(phase))
    return out


def square(n, f0, f1=None, duty=0.5):
    f1 = f0 if f1 is None else f1
    out, phase = [], 0.0
    for i in range(n):
        f = f0 + (f1 - f0) * (i / max(n - 1, 1))
        phase += f / SR
        out.append(1.0 if (phase % 1.0) < duty else -1.0)
    return out


def lowpass(sig, alpha):
    """One-pole low pass. alpha ~0.02 = very dark, ~0.5 = bright."""
    out, y = [], 0.0
    for x in sig:
        y += alpha * (x - y)
        out.append(y)
    return out


def highpass(sig, alpha):
    return [x - y for x, y in zip(sig, lowpass(sig, alpha))]


def decay(n, rate=5.0):
    return [math.exp(-rate * i / max(n - 1, 1)) for i in range(n)]


def attack(n, seconds=0.005):
    """Short fade-in so samples don't start with a click."""
    ramp = max(int(SR * seconds), 1)
    return [min(1.0, i / ramp) for i in range(n)]


def apply(sig, *envelopes):
    out = list(sig)
    for env in envelopes:
        out = [s * e for s, e in zip(out, env)]
    return out


def mix(*signals):
    length = max(len(s) for s in signals)
    out = [0.0] * length
    for sig in signals:
        for i, s in enumerate(sig):
            out[i] += s
    return out


def normalize(sig, peak=0.85):
    hi = max((abs(s) for s in sig), default=0.0)
    if hi < 1e-6:
        return sig
    k = peak / hi
    return [s * k for s in sig]


def write(name, sig):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT_DIR, name))
    sig = normalize(sig)
    frames = bytearray()
    for s in sig:
        v = max(-1.0, min(1.0, s))
        frames += struct.pack("<h", int(v * 32767))
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))
    print(f"  {name}  ({len(sig) / SR:.2f}s)")


# ── The sounds ───────────────────────────────────────────────────────────────

def pistol():
    n = n_samples(0.16)
    crack = apply(highpass(noise(n), 0.35), decay(n, 22.0))
    body = apply(sine(n, 320, 90), decay(n, 26.0))
    return apply(mix(crack, [b * 0.8 for b in body]), attack(n))


def shotgun():
    n = n_samples(0.38)
    blast = apply(lowpass(noise(n), 0.25), decay(n, 9.0))
    thump = apply(sine(n, 190, 45), decay(n, 11.0))
    return apply(mix(blast, [t * 1.1 for t in thump]), attack(n))


def machinegun():
    n = n_samples(0.10)
    crack = apply(highpass(noise(n), 0.45), decay(n, 34.0))
    body = apply(sine(n, 420, 140), decay(n, 38.0))
    return apply(mix(crack, [b * 0.6 for b in body]), attack(n))


def knife():
    n = n_samples(0.18)
    swish = apply(highpass(noise(n), 0.12), decay(n, 14.0))
    # brief rising whistle to suggest the arc of the swing
    air = apply(sine(n, 700, 1500), decay(n, 16.0))
    return apply(mix(swish, [a * 0.25 for a in air]), attack(n))


def explosion():
    n = n_samples(0.95)
    rumble = apply(lowpass(noise(n), 0.06), decay(n, 4.2))
    boom = apply(sine(n, 130, 28), decay(n, 5.0))
    debris = apply(highpass(noise(n), 0.5), decay(n, 12.0))
    return apply(mix(rumble, [b * 1.2 for b in boom], [d * 0.3 for d in debris]), attack(n))


def hit():
    n = n_samples(0.12)
    thud = apply(lowpass(noise(n), 0.18), decay(n, 28.0))
    low = apply(sine(n, 240, 110), decay(n, 30.0))
    return apply(mix(thud, [l * 0.7 for l in low]), attack(n))


def enemy_death():
    n = n_samples(0.42)
    groan = apply(square(n, 190, 55, duty=0.35), decay(n, 7.0))
    wet = apply(lowpass(noise(n), 0.14), decay(n, 9.0))
    return apply(mix([g * 0.6 for g in groan], [w * 0.7 for w in wet]), attack(n))


def player_hurt():
    n = n_samples(0.30)
    tone = apply(square(n, 300, 120, duty=0.4), decay(n, 10.0))
    body = apply(lowpass(noise(n), 0.2), decay(n, 16.0))
    return apply(mix([t * 0.7 for t in tone], [b * 0.5 for b in body]), attack(n))


def pickup_ammo():
    n = n_samples(0.14)
    blip = apply(square(n, 620, 1180, duty=0.5), decay(n, 12.0))
    return apply(blip, attack(n, 0.002))


def pickup_health():
    seg = n_samples(0.09)
    a = apply(square(seg, 660, 660), decay(seg, 8.0))
    b = apply(square(seg, 880, 880), decay(seg, 8.0))
    c = apply(square(seg, 1320, 1320), decay(seg, 7.0))
    out = a + b + c
    return apply(out, attack(len(out), 0.002))


def barrel_place():
    n = n_samples(0.16)
    knock = apply(lowpass(noise(n), 0.11), decay(n, 24.0))
    wood = apply(sine(n, 210, 150), decay(n, 20.0))
    return apply(mix(knock, [w * 0.9 for w in wood]), attack(n))


def wave_start():
    seg = n_samples(0.14)
    notes = [523, 659, 784, 1047]  # C-E-G-C major arpeggio
    out = []
    for i, f in enumerate(notes):
        rate = 6.0 if i < len(notes) - 1 else 3.0
        out += apply(square(seg, f, f, duty=0.5), decay(seg, rate))
    return apply(out, attack(len(out), 0.003))


def game_over():
    seg = n_samples(0.26)
    notes = [392, 330, 262, 196]  # descending, resolves low
    out = []
    for i, f in enumerate(notes):
        rate = 5.0 if i < len(notes) - 1 else 2.2
        out += apply(square(seg, f, f, duty=0.45), decay(seg, rate))
    return apply(out, attack(len(out), 0.004))


SOUNDS = {
    "pistol.wav":        pistol,
    "shotgun.wav":       shotgun,
    "machinegun.wav":    machinegun,
    "knife.wav":         knife,
    "explosion.wav":     explosion,
    "hit.wav":           hit,
    "enemy_death.wav":   enemy_death,
    "player_hurt.wav":   player_hurt,
    "pickup_ammo.wav":   pickup_ammo,
    "pickup_health.wav": pickup_health,
    "barrel_place.wav":  barrel_place,
    "wave_start.wav":    wave_start,
    "game_over.wav":     game_over,
}

if __name__ == "__main__":
    print("Generating placeholder SFX:")
    for filename, builder in SOUNDS.items():
        write(filename, builder())
    print(f"\nWrote {len(SOUNDS)} files to {os.path.normpath(OUT_DIR)}")
