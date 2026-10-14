#!/usr/bin/env python3
"""Synthesises AVASPYHunter's sound effects and tunes into ../src/AVASPYHunter/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASPYHunter", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    # Guns, hits and wrecks.
    write("Shoot.wav", mix(tone(0.05, 1200, 300, "noise", release=1.2), tone(0.04, 220, 120, "square", release=1.0, gain=0.4)), 0.2)
    write("Hit.wav", tone(0.06, 900, 400, "square", release=1.2), 0.25)
    write("Ricochet.wav", tone(0.25, 3200, 1400, "tri", release=0.7, vibrato=0.05, vib_rate=40), 0.25)
    write("Explode.wav", mix(tone(0.9, 300, 30, "noise", release=0.5), tone(0.7, 110, 30, "square", release=0.6, gain=0.6)), 0.6)
    write("Crash.wav", cat(mix(tone(0.15, 2400, 600, "noise", release=0.6), tone(0.15, 300, 100, "square", release=0.8)),
                           mix(tone(1.0, 300, 25, "noise", release=0.45), tone(0.8, 120, 25, "saw", release=0.6, gain=0.6))), 0.65)
    write("Skid.wav", tone(0.6, 2200, 1500, "noise", release=0.4, vibrato=0.1, vib_rate=25), 0.35)
    write("Splash.wav", mix(tone(0.6, 1500, 200, "noise", release=0.6), tone(0.4, 300, 90, "sine", release=0.8, gain=0.4)), 0.45)
    write("Bump.wav", mix(tone(0.08, 160, 90, "square", release=0.8), tone(0.06, 900, 300, "noise", release=0.9, gain=0.5)), 0.35)
    # The armoury and its weapons.
    write("Oil.wav", cat(tone(0.08, 300, 200, "noise", release=0.8), tone(0.25, 180, 120, "sine", release=0.6, gain=0.5)), 0.35)
    write("Smoke.wav", tone(0.9, 600, 200, "noise", attack=0.05, release=0.4), 0.3)
    write("Missile.wav", cat(tone(0.08, 400, 900, "saw", release=0.4, gain=0.5), tone(0.6, 900, 300, "noise", release=0.6)), 0.4)
    write("Dock.wav", cat(*[tone(0.07, f, kind="square", release=0.6) for f in [330, 440, 550]], tone(0.3, 200, 120, "noise", release=0.8)), 0.4)
    write("Weapon.wav", cat(*[tone(0.07, hz(n), kind="tri", release=0.4) for n in ["E5", "G5", "B5", "E6"]]), 0.45)
    # The helicopter and its bombs.
    write("Copter.wav", cat(*[mix(tone(0.07, 120, 90, "noise", release=0.9), tone(0.07, 80, 70, "square", release=1.0, gain=0.4)) for _ in range(14)]), 0.4)
    write("BombDrop.wav", tone(0.8, 1800, 500, "sine", release=0.3), 0.3)
    write("BombBlast.wav", mix(tone(1.1, 200, 25, "noise", release=0.4), tone(0.9, 80, 25, "square", release=0.6, gain=0.6)), 0.7)
    # Changing from car to boat and back.
    write("Boat.wav", cat(tone(0.3, 300, 200, "noise", release=0.6), tone(0.4, hz("E4"), kind="tri", release=0.5), tone(0.5, hz("B4"), kind="tri", release=0.7)), 0.4)
    write("Car.wav", cat(tone(0.25, 90, 180, "saw", release=0.3, gain=0.6), tone(0.4, 180, 140, "saw", release=0.6, gain=0.6)), 0.35)
    write("NoScore.wav", cat(tone(0.25, 220, 210, "square", release=0.3), tone(0.5, 165, 155, "square", release=0.5)), 0.35)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E6", "B5", "E6", "B5", "E6", "G6", "B6"]]), 0.4)

    # Driving: a moody surf-guitar ostinato in E minor over a walking bass, with an engine growl (new). Loops.
    riff = [("E3", 0.5), ("E3", 0.25), ("G3", 0.25), ("A3", 0.5), ("E3", 0.5), ("D3", 0.5), ("E3", 0.5), ("B2", 0.5), ("D3", 0.5)]
    lead = [("-", 8), ("B4", 1), ("A4", 0.5), ("G4", 0.5), ("E4", 2), ("-", 1), ("D4", 0.5), ("E4", 0.5), ("G4", 1), ("A4", 1), ("B4", 2), ("-", 1),
            ("D5", 1), ("C5", 0.5), ("B4", 0.5), ("A4", 2), ("G4", 1), ("A4", 1), ("E4", 4)]
    bass = seq(138, riff * 8, kind="square", gain=0.35, release=0.5)
    top = seq(138, lead, kind="saw", gain=0.28, release=0.4)
    drums = cat(*[mix(tone(0.08, 120, 50, "sine", release=0.9, gain=0.8), silence(0.2)) + silence(0.13) + mix(tone(0.05, 6000, 3000, "noise", release=1.0, gain=0.25), silence(0.2)) + silence(0.07) for _ in range(int(len(bass) / RATE / 0.435))])
    write("Music.wav", mix(bass, top, drums), 0.42)

    write("TitleTune.wav", tune(126,
                                [("E4", 0.5), ("G4", 0.5), ("B4", 1), ("A4", 0.5), ("G4", 0.5), ("E4", 1), ("D4", 0.5), ("E4", 0.5), ("G4", 1), ("B4", 2),
                                 ("C5", 0.5), ("B4", 0.5), ("A4", 1), ("G4", 0.5), ("A4", 0.5), ("B4", 1), ("E4", 4)],
                                [("E2", 2), ("E2", 2), ("C2", 2), ("D2", 2), ("A1", 2), ("B1", 2), ("E2", 4)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("B3", 0.5), ("E4", 0.5), ("G4", 0.5), ("B4", 1.5), ("A4", 0.5), ("B4", 2)], [("E2", 2), ("B1", 1), ("E2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(84, [("B4", 1), ("A4", 1), ("G4", 1), ("F#4", 1), ("E4", 3)], [("E3", 2), ("C3", 2), ("B2", 1), ("E2", 3)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
