#!/usr/bin/env python3
"""Bake original deterministic presentation cues. No runtime/network dependency."""
from __future__ import annotations
import hashlib, math, random, struct, wave
from pathlib import Path

CUES = [
 "ui.button.click","ui.command.submit","card.play","card.draw","combat.damage","combat.heal","combat.death",
 "trigger.deathrattle","trigger.secret","trigger.quest","modifier.buff","modifier.expire","modifier.aura",
 "turn.start","turn.end","turn.extra","choice.open","choice.resolve","connection.reconnect.success",
 "connection.reconnect.fail","replay.verified","replay.mismatch","fault.blocking","equipment.activate",
 "modifier.trigger","hero.action","game.start","event.generic"
]
RATE=44100
ROOT=Path(__file__).resolve().parents[2]/"clients/unity/CardGame.Unity/Assets/CardGame/Resources/Audio/Presentation"

def sample(cue: str, t: float, duration: float, rng: random.Random) -> float:
    p=t/duration; seed=int(hashlib.sha256(cue.encode()).hexdigest()[:8],16)
    base=130+(seed%390); attack=min(1,t/.018); release=(1-p)**(1.8 if "heal" in cue or "quest" in cue else .85)
    if "damage" in cue or "death" in cue or "fault" in cue:
        sweep=base*(1-.66*p); body=math.sin(math.tau*sweep*t)+.42*math.sin(math.tau*sweep*1.73*t)
        noise=(rng.random()*2-1)*(.85*(1-p)**2); return (body*.55+noise)*attack*release*.38
    if "secret" in cue or "aura" in cue or "choice" in cue:
        sweep=base*(1+1.4*p); shimmer=sum(math.sin(math.tau*sweep*r*t)/(r*r) for r in (1,1.5,2.5,4))
        return shimmer*attack*release*.22
    if "turn" in cue or "game.start" in cue or "hero" in cue:
        chord=sum(math.sin(math.tau*base*r*t) for r in (1,1.25,1.5,2))/4
        pulse=.65+.35*math.sin(math.tau*6*t); return chord*attack*release*pulse*.3
    if "card" in cue or "equipment" in cue:
        scrape=(rng.random()*2-1)*math.exp(-t*20); tone=math.sin(math.tau*(base+180*p)*t)
        return (scrape*.5+tone*.5)*attack*release*.32
    sweep=base*(1+.35*p); return math.sin(math.tau*sweep*t)*attack*release*.27

def main() -> None:
    ROOT.mkdir(parents=True,exist_ok=True)
    for cue in CUES:
        duration=.82 if any(k in cue for k in ("deathrattle","game.start","fault")) else .52 if any(k in cue for k in ("secret","quest","hero","turn")) else .28
        rng=random.Random(cue); frames=[]
        for i in range(int(RATE*duration)):
            value=max(-1,min(1,sample(cue,i/RATE,duration,rng)))
            left=int(value*32767); right=int(value*(.96+.04*math.sin(i*.0017))*32767)
            frames.append(struct.pack("<hh",left,right))
        path=ROOT/(cue.replace(".","-")+".wav")
        with wave.open(str(path),"wb") as out:
            out.setnchannels(2);out.setsampwidth(2);out.setframerate(RATE);out.writeframes(b"".join(frames))
        print(path.relative_to(ROOT.parents[5]))

if __name__ == "__main__": main()
