"""
Embervale procedural audio generator.

Synthesises every sound effect, music loop and ambience loop the client uses, from code only (numpy +
the standard library). Output is deterministic (fixed seeds), so re-running produces identical files.

    python Tools/Audio/generate_audio.py            # writes Assets/Audio/{Sfx,Music,Ambient}/*.wav
    python Tools/Audio/generate_audio.py --only sfx # just one group

All sounds are original: simple synthesis (Karplus-Strong plucks, FM bells, filtered noise, sine pads)
and short algorithmically composed melodies over fixed chord progressions.
"""
import argparse
import json
import os
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Assets", "Audio")
SR_SFX = 44100
SR_MUSIC = 22050


# ---------------------------------------------------------------------------- helpers
def write_wav(path, x, sr):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = np.asarray(x, dtype=np.float64)
    peak = np.max(np.abs(x)) if x.size else 1.0
    if peak > 0:
        x = x / peak * 0.89
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data.tobytes())


def t_axis(dur, sr):
    return np.arange(int(dur * sr)) / sr


def env_adsr(n, sr, a=0.005, d=0.05, s=0.6, r=0.1):
    e = np.ones(n) * s
    na, nd, nr = int(a * sr), int(d * sr), int(r * sr)
    na = max(1, min(na, n)); nd = max(1, min(nd, n - na)); nr = max(1, min(nr, n))
    e[:na] = np.linspace(0, 1, na)
    e[na:na + nd] = np.linspace(1, s, nd)
    e[-nr:] *= np.linspace(1, 0, nr)
    return e


def env_exp(n, sr, decay):
    return np.exp(-np.arange(n) / sr / max(decay, 1e-4))


def lowpass(x, cutoff, sr):
    """One-pole low-pass (cutoff may be an array for sweeps)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = 1 - np.exp(-2 * np.pi * cutoff / sr)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def lowpass_fast(x, cutoff, sr):
    """Constant-cutoff one-pole low-pass using scipy-free recursion in blocks (fast enough for loops)."""
    a = 1 - np.exp(-2 * np.pi * cutoff / sr)
    b = 1 - a
    # Implement y[n] = a*x[n] + b*y[n-1] with cumulative trick in chunks to stay numerically stable.
    y = np.empty_like(x)
    prev = 0.0
    if b <= 1e-12:
        return x * a
    chunk = int(max(16, min(2048, -250.0 / np.log10(b))))
    powers = b ** np.arange(1, chunk + 1)
    for s in range(0, len(x), chunk):
        seg = x[s:s + chunk] * a
        m = len(seg)
        p = powers[:m]
        # y_k = sum_{j<=k} seg_j * b^(k-j) + prev * b^(k+1)
        scaled = seg / (b ** np.arange(m))
        y[s:s + m] = np.cumsum(scaled) * (b ** np.arange(m)) + prev * p
        prev = y[s + m - 1]
    return y


def highpass_fast(x, cutoff, sr):
    return x - lowpass_fast(x, cutoff, sr)


def noise(n, rng):
    return rng.uniform(-1, 1, n)


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def pluck(freq, dur, sr, rng, bright=0.5, decay=0.996):
    """Karplus-Strong plucked string, vectorised per period."""
    n = int(dur * sr)
    period = max(2, int(sr / freq))
    buf = noise(period, rng) * 0.8
    buf = lowpass_fast(buf, 1000 + bright * 6000, sr)
    out = np.zeros(n)
    pos = 0
    while pos < n:
        m = min(period, n - pos)
        out[pos:pos + m] = buf[:m]
        nxt = decay * 0.5 * (buf + np.roll(buf, 1))
        buf = nxt
        pos += period
    return out * env_adsr(n, sr, 0.002, 0.02, 1.0, 0.05)


def fm_bell(freq, dur, sr, ratio=3.5, index=3.0, decay=0.8):
    t = t_axis(dur, sr)
    e = np.exp(-t / decay)
    mod = np.sin(2 * np.pi * freq * ratio * t) * index * e
    return np.sin(2 * np.pi * freq * t + mod) * e


def flute(freq, dur, sr, rng, vibrato=5.0):
    t = t_axis(dur, sr)
    vib = 1 + 0.006 * np.sin(2 * np.pi * vibrato * t) * np.clip(t * 3, 0, 1)
    ph = 2 * np.pi * np.cumsum(freq * vib) / sr
    tone = np.sin(ph) + 0.18 * np.sin(2 * ph) + 0.06 * np.sin(3 * ph)
    breath = lowpass_fast(noise(len(t), rng), 2500, sr) * 0.06
    return (tone + breath) * env_adsr(len(t), sr, 0.06, 0.1, 0.8, min(0.15, dur * 0.4))


def pad(freqs, dur, sr, rng, cutoff=1200):
    t = t_axis(dur, sr)
    x = np.zeros(len(t))
    for f in freqs:
        for det in (-0.004, 0.0, 0.005):
            ph = rng.uniform(0, 1)
            saw = 2 * ((t * f * (1 + det) + ph) % 1) - 1
            x += saw
    x = lowpass_fast(x / (len(freqs) * 3), cutoff, sr)
    return x * env_adsr(len(t), sr, min(0.8, dur * 0.3), 0.2, 0.9, min(1.0, dur * 0.3))


def drum(dur, sr, rng, f0=110, f1=50, noise_amt=0.2, decay=0.25):
    t = t_axis(dur, sr)
    f = f1 + (f0 - f1) * np.exp(-t * 30)
    body = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.exp(-t / decay)
    hit = lowpass_fast(noise(len(t), rng), 3000, sr) * np.exp(-t * 60) * noise_amt
    return body + hit


def mix_at(dst, src, start, gain=1.0):
    s = int(start)
    if s >= len(dst):
        return
    e = min(len(dst), s + len(src))
    dst[s:e] += src[:e - s] * gain


# ---------------------------------------------------------------------------- sound effects
def sfx_all():
    sr = SR_SFX
    rng = np.random.default_rng(1001)
    out = {}

    def nz(d, cut, dec, hp=0):
        n = int(d * sr)
        x = noise(n, rng)
        x = lowpass_fast(x, cut, sr)
        if hp:
            x = highpass_fast(x, hp, sr)
        return x * env_exp(n, sr, dec)

    def S(*parts):
        """Sum arrays of different lengths (shorter ones are zero-padded)."""
        n = max(len(p) for p in parts)
        y = np.zeros(n)
        for p in parts:
            y[:len(p)] += p
        return y

    def at(x, sec):
        return np.concatenate([np.zeros(int(sec * sr)), x])

    out["click"] = fm_bell(1800, 0.06, sr, ratio=2, index=1, decay=0.015)
    out["ui_open"] = np.concatenate([fm_bell(660, 0.07, sr, 2, 1, 0.05), fm_bell(990, 0.12, sr, 2, 1, 0.07)])
    out["swing"] = nz(0.28, 2500, 0.08, 400) * np.sin(np.linspace(0, np.pi, int(0.28 * sr)))
    out["hit"] = S(drum(0.25, sr, rng, 160, 60, 0.9, 0.06), nz(0.2, 4000, 0.04) * 0.6)
    out["hit_crit"] = S(drum(0.35, sr, rng, 220, 50, 1.2, 0.09), nz(0.3, 6000, 0.06) * 0.8, fm_bell(1200, 0.3, sr, 3.1, 2, 0.1) * 0.3)
    out["miss"] = nz(0.22, 1800, 0.06, 600) * 0.6
    out["block"] = S(fm_bell(520, 0.3, sr, 2.7, 4, 0.08), nz(0.15, 5000, 0.03) * 0.4)
    out["arrow"] = S(nz(0.35, 6000, 0.12, 1500) * np.linspace(1, 0.2, int(0.35 * sr)), pluck(180, 0.35, sr, rng, 0.8, 0.98) * 0.5)
    out["death"] = S(drum(0.5, sr, rng, 90, 35, 0.6, 0.3), fm_bell(220, 0.5, sr, 1.5, 1, 0.3) * 0.3)
    # spells: element-coloured whooshes
    for el, (f, cut, bell) in {"air": (900, 7000, 0.2), "water": (500, 2500, 0.4), "earth": (120, 900, 0.1), "fire": (300, 5000, 0.2),
                               "life": (700, 4000, 0.6), "void": (160, 1500, 0.5), "path": (1100, 6000, 0.6), "spirit": (640, 3500, 0.7)}.items():
        n = int(0.6 * sr)
        sweep = np.linspace(cut * 0.3, cut, n)
        whoosh = lowpass(noise(n, rng), sweep, sr) * np.sin(np.linspace(0, np.pi, n)) ** 0.7
        out["spell_" + el] = S(whoosh, fm_bell(f, 0.6, sr, 1.41, 2.5, 0.25) * bell)
    out["telegraph"] = S(fm_bell(196, 0.9, sr, 1.01, 6, 0.5), fm_bell(233, 0.9, sr, 1.01, 6, 0.5)) * 0.5
    out["level_up"] = np.concatenate([fm_bell(midi_hz(m), 0.16, sr, 2, 1.5, 0.25) for m in (72, 76, 79)] + [fm_bell(midi_hz(84), 0.9, sr, 2, 1.5, 0.5)])
    out["eat"] = np.concatenate([nz(0.07, 3000, 0.02, 800), np.zeros(int(0.08 * sr)), nz(0.07, 2600, 0.02, 800), np.zeros(int(0.06 * sr)), nz(0.06, 3200, 0.02, 800)])
    n = int(0.5 * sr)
    gl = np.zeros(n)
    for k in range(5):
        mix_at(gl, fm_bell(500 + 90 * k, 0.12, sr, 1, 0.5, 0.03), k * 0.09 * sr, 0.7)
    out["drink"] = gl
    out["mine"] = S(fm_bell(1400, 0.4, sr, 2.76, 5, 0.09), drum(0.2, sr, rng, 300, 120, 1.0, 0.03) * 0.6)
    out["chop"] = S(drum(0.3, sr, rng, 180, 70, 1.4, 0.05), nz(0.2, 1500, 0.05) * 0.8)
    n = int(0.7 * sr)
    out["splash"] = lowpass(noise(n, rng), np.linspace(5000, 600, n), sr) * env_exp(n, sr, 0.18)
    out["anvil"] = S(fm_bell(880, 0.8, sr, 2.4, 6, 0.25), fm_bell(1320, 0.6, sr, 3.1, 3, 0.15) * 0.5)
    out["pray"] = pad([midi_hz(60), midi_hz(64), midi_hz(67), midi_hz(72)], 1.6, sr, rng, 1800) * 0.8
    out["coins"] = S(*[at(fm_bell(2400 + 300 * k, 0.15, sr, 1.5, 2, 0.05), k * 0.05) for k in range(4)])
    out["drop"] = drum(0.15, sr, rng, 260, 130, 0.5, 0.04)
    out["pickup"] = np.concatenate([fm_bell(880, 0.06, sr, 2, 1, 0.03), fm_bell(1175, 0.1, sr, 2, 1, 0.05)])
    n = int(0.6 * sr)
    creak = lowpass(noise(n, rng) * np.sin(2 * np.pi * 7 * t_axis(0.6, sr)) ** 2, np.linspace(1200, 400, n), sr)
    out["door_open"] = S(creak, at(drum(0.2, sr, rng, 120, 60, 0.4, 0.06), 0.4))
    out["door_close"] = S(drum(0.35, sr, rng, 140, 55, 0.8, 0.09), nz(0.2, 1200, 0.05) * 0.4)
    out["woodcutting_success"] = S(out["chop"], at(fm_bell(660, 0.3, sr, 2, 1, 0.12), 0.05) * 0.4)
    out["mining_success"] = S(out["mine"], fm_bell(990, 0.4, sr, 2, 1, 0.15) * 0.4)
    out["fishing_success"] = S(out["splash"], fm_bell(784, 0.3, sr, 2, 1, 0.12) * 0.4)
    success = np.concatenate([fm_bell(midi_hz(76), 0.1, sr, 2, 1, 0.08), fm_bell(midi_hz(81), 0.25, sr, 2, 1, 0.15)])
    for skill in ("farming", "thieving", "agility", "runecrafting", "prayer"):
        out[skill + "_success"] = success
    out["smithing"] = out["anvil"]
    n = int(0.9 * sr)
    out["cooking"] = lowpass_fast(noise(n, rng), 6000, sr) * (0.6 + 0.4 * rng.uniform(0, 1, n)) * env_adsr(n, sr, 0.05, 0.1, 0.8, 0.3)
    out["crafting"] = S(nz(0.12, 3500, 0.04, 500), at(nz(0.12, 3000, 0.04, 500), 0.15))
    out["fletching"] = nz(0.3, 5000, 0.1, 1200) * np.linspace(1, 0.3, int(0.3 * sr))
    out["herblore"] = S(gl * 0.7, fm_bell(1046, 0.3, sr, 1.5, 1, 0.1) * 0.3)
    out["runecrafting"] = out["spell_path"]
    n = int(1.0 * sr)
    out["firemaking"] = S(lowpass_fast(noise(n, rng), 3000, sr) * (rng.uniform(0, 1, n) > 0.995) * 3, lowpass_fast(noise(n, rng), 600, sr) * env_adsr(n, sr, 0.3, 0.2, 0.7, 0.3))
    out["footstep"] = nz(0.08, 900, 0.02)
    return out, sr


# ---------------------------------------------------------------------------- music
SCALES = {"major": [0, 2, 4, 5, 7, 9, 11], "minor": [0, 2, 3, 5, 7, 8, 10], "dorian": [0, 2, 3, 5, 7, 9, 10],
          "mixolydian": [0, 2, 4, 5, 7, 9, 10], "phrygian": [0, 1, 3, 5, 7, 8, 10], "pentatonic": [0, 2, 4, 7, 9]}

# region -> (root midi, scale, bpm, progression in scale degrees, lead instrument, mood flags)
MUSIC = {
    "title": (57, "dorian", 78, [0, 5, 3, 4], "flute", {"pad": True, "drum": False, "bell": True}),
    "village": (60, "major", 96, [0, 3, 4, 0, 5, 3, 4, 4], "pluck", {"pad": True, "drum": True, "bell": False}),
    "forest": (62, "dorian", 84, [0, 6, 3, 0, 0, 6, 4, 4], "flute", {"pad": True, "drum": False, "bell": True}),
    "city": (55, "mixolydian", 104, [0, 6, 3, 0, 4, 3, 6, 0], "pluck", {"pad": True, "drum": True, "bell": True}),
    "mountain": (50, "minor", 72, [0, 5, 2, 6, 0, 3, 4, 4], "flute", {"pad": True, "drum": True, "bell": False}),
    "marsh": (52, "phrygian", 64, [0, 1, 0, 6, 0, 1, 3, 1], "bell", {"pad": True, "drum": False, "bell": False}),
    "dungeon": (45, "minor", 70, [0, 0, 5, 4, 0, 0, 1, 4], "bell", {"pad": True, "drum": True, "bell": False}),
    "meadow": (64, "major", 88, [0, 4, 5, 3, 0, 4, 3, 4], "flute", {"pad": True, "drum": False, "bell": True}),
}


def compose(name, spec, sr, bars_per_chord=1, repeats=2):
    root, scale_name, bpm, prog, lead, flags = spec
    rng = np.random.default_rng(abs(hash(name)) % (2 ** 32) if False else sum(map(ord, name)) * 7919)
    scale = SCALES[scale_name]
    beat = 60.0 / bpm
    bar = beat * 4
    total_bars = len(prog) * bars_per_chord * repeats
    dur = total_bars * bar
    n = int(dur * sr) + int(2 * sr)
    music = np.zeros(n)

    def degree_to_midi(deg, octave=0):
        o, d = divmod(deg, len(scale))
        return root + scale[d] + 12 * (o + octave)

    # melody motif generated once and varied per repeat
    motif = []
    pos = 0.0
    while pos < 4:
        length = rng.choice([0.5, 1, 1, 1.5, 2]) if lead != "pluck" else rng.choice([0.5, 0.5, 1, 1])
        length = min(length, 4 - pos)
        motif.append((pos, length, int(rng.integers(0, 5))))
        pos += length

    for b in range(total_bars):
        chord_deg = prog[(b // bars_per_chord) % len(prog)]
        t0 = b * bar
        chord = [degree_to_midi(chord_deg + k, -1) for k in (0, 2, 4)]
        if flags.get("pad"):
            mix_at(music, pad([midi_hz(m) for m in chord], bar * 1.05, sr, rng, 900 if scale_name in ("minor", "phrygian") else 1300), t0 * sr, 0.35)
        # bass
        mix_at(music, pluck(midi_hz(chord[0] - 12), bar * 0.9, sr, rng, 0.2, 0.997), t0 * sr, 0.5)
        if flags.get("drum"):
            for k in range(4):
                if k % 2 == 0:
                    mix_at(music, drum(0.4, sr, rng, 90, 45, 0.25, 0.18), (t0 + k * beat) * sr, 0.45)
                else:
                    mix_at(music, drum(0.2, sr, rng, 240, 160, 0.5, 0.05), (t0 + k * beat) * sr, 0.18)
        # arpeggio for plucky tracks
        if lead == "pluck" or flags.get("bell"):
            for k in range(8):
                m = chord[k % 3] + 12 * (1 + (k // 3) % 2)
                inst = pluck(midi_hz(m), beat, sr, rng, 0.6, 0.994) if lead == "pluck" else fm_bell(midi_hz(m + 12), beat * 1.5, sr, 3.0, 1.2, 0.4) * 0.5
                mix_at(music, inst, (t0 + k * beat / 2) * sr, 0.22)
        # lead melody (rests every other phrase so loops breathe)
        phrase = (b // 2) % 4
        if phrase in (1, 3) or b % 2 == 1:
            for (p, length, step) in motif:
                if rng.uniform() < 0.15:
                    continue
                deg = chord_deg + [0, 2, 4, 1, 3][step] + (1 if phrase == 3 and step == 4 else 0)
                m = degree_to_midi(deg, 1)
                d = length * beat
                if lead == "flute":
                    note = flute(midi_hz(m), d * 0.95, sr, rng)
                elif lead == "bell":
                    note = fm_bell(midi_hz(m), d * 1.8, sr, 3.5, 2.0, 0.6)
                else:
                    note = pluck(midi_hz(m), d * 1.2, sr, rng, 0.8, 0.995)
                mix_at(music, note, (t0 + p * beat) * sr, 0.38)

    # Loop seam: fold the tail (reverb-ish ring) back onto the start.
    loop_len = int(dur * sr)
    tail = music[loop_len:]
    music = music[:loop_len]
    music[:len(tail)] += tail
    # gentle room: a few feedback-free delays
    room = np.zeros_like(music)
    for d, g in ((0.031, 0.25), (0.053, 0.2), (0.089, 0.15), (0.137, 0.1)):
        k = int(d * sr)
        room[k:] += music[:-k] * g
        room[:k] += music[-k:] * g
    return music + room


# ---------------------------------------------------------------------------- ambience
def ambient(name, sr, dur=24.0):
    rng = np.random.default_rng(sum(map(ord, name)) * 104729)
    n = int(dur * sr)
    x = np.zeros(n)

    def bird(start, base):
        d = rng.uniform(0.08, 0.2)
        t = t_axis(d, sr)
        f = base * (1 + 0.3 * np.sin(2 * np.pi * rng.uniform(8, 20) * t))
        s = np.sin(2 * np.pi * np.cumsum(f) / sr) * np.sin(np.pi * t / d)
        mix_at(x, s, start * sr, rng.uniform(0.05, 0.15))

    def wind(level, cut):
        w = lowpass_fast(noise(n, rng), cut, sr)
        swell = 0.6 + 0.4 * np.sin(2 * np.pi * t_axis(dur, sr) / dur * 3 + rng.uniform(0, 6))
        return w * swell * level

    if name in ("forest", "meadow", "village"):
        x += wind(0.25, 500)
        for _ in range(int(dur * (1.6 if name == "forest" else 0.9))):
            s = rng.uniform(0, dur - 1)
            base = rng.uniform(2200, 4200)
            for k in range(int(rng.integers(1, 4))):
                bird(s + k * 0.14, base * rng.uniform(0.95, 1.05))
        if name == "village":
            for _ in range(6):
                mix_at(x, drum(0.2, sr, rng, 300, 200, 0.3, 0.04), rng.uniform(0, dur - 1) * sr, 0.06)
    elif name == "city":
        x += lowpass_fast(noise(n, rng), 900, sr) * 0.35
        for _ in range(int(dur * 1.5)):
            mix_at(x, drum(0.12, sr, rng, 350, 250, 0.6, 0.03), rng.uniform(0, dur - 1) * sr, 0.08)
        for _ in range(3):
            mix_at(x, fm_bell(rng.choice([392, 440, 523]), 1.5, sr, 2.4, 3, 0.6), rng.uniform(0, dur - 2) * sr, 0.05)
    elif name == "wind":
        x += wind(1.0, 700) + wind(0.4, 2500)
    elif name == "marsh":
        x += wind(0.2, 400)
        for _ in range(int(dur * 2)):
            # frog croaks
            d = 0.12
            t = t_axis(d, sr)
            croak = np.sign(np.sin(2 * np.pi * rng.uniform(90, 160) * t)) * np.sin(np.pi * t / d) * (np.sin(2 * np.pi * 30 * t) > 0)
            mix_at(x, lowpass_fast(croak, 900, sr), rng.uniform(0, dur - 1) * sr, 0.12)
        for _ in range(int(dur * 0.8)):
            mix_at(x, lowpass(noise(int(0.3 * sr), rng), np.linspace(2500, 400, int(0.3 * sr)), sr) * env_exp(int(0.3 * sr), sr, 0.08), rng.uniform(0, dur - 1) * sr, 0.15)
    elif name == "ruins":
        x += wind(0.6, 350)
        for _ in range(int(dur * 0.5)):
            mix_at(x, fm_bell(rng.uniform(800, 1600), 0.6, sr, 1.0, 0.2, 0.15) * 0.3, rng.uniform(0, dur - 1) * sr, 0.2)  # drips
        x += pad([55, 55 * 1.5], dur, sr, rng, 250) * 0.3
    # cross-fade the ends so the loop is seamless
    k = int(1.0 * sr)
    fade = np.linspace(0, 1, k)
    x[:k] = x[:k] * fade + x[-k:] * (1 - fade)
    return x[:-k]


# ---------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", choices=["sfx", "music", "ambient"], default=None)
    args = ap.parse_args()
    manifest = {"generator": "Tools/Audio/generate_audio.py", "sfx": [], "music": [], "ambient": []}
    if args.only in (None, "sfx"):
        sounds, sr = sfx_all()
        for name, x in sounds.items():
            write_wav(os.path.join(OUT, "Sfx", name + ".wav"), x, sr)
            manifest["sfx"].append(name)
        print("sfx:", len(sounds))
    if args.only in (None, "music"):
        for name, spec in MUSIC.items():
            x = compose(name, spec, SR_MUSIC, bars_per_chord=1, repeats=3 if len(spec[3]) <= 4 else 2)
            write_wav(os.path.join(OUT, "Music", "music_" + name + ".wav"), x, SR_MUSIC)
            manifest["music"].append("music_" + name)
            print("music:", name, "%.1fs" % (len(x) / SR_MUSIC))
    if args.only in (None, "ambient"):
        for name in ("village", "forest", "city", "wind", "marsh", "ruins", "meadow"):
            x = ambient(name, SR_MUSIC)
            write_wav(os.path.join(OUT, "Ambient", "amb_" + name + ".wav"), x, SR_MUSIC)
            manifest["ambient"].append("amb_" + name)
        print("ambient: 7")
    if args.only is None:
        with open(os.path.join(REPO, "Tools", "Blender", "AssetDocumentation", "manifest_audio.json"), "w") as f:
            json.dump(manifest, f, indent=1)


if __name__ == "__main__":
    main()
