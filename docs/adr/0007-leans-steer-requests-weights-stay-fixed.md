# Leans steer one Request; the scoring weights stay fixed

The UI offers four Leans (Greenery, Off-road, Hills, Novelty) and a Heading control. A Lean's centre is the calibrated weight in `ScoringWeights`. Moving it multiplies that one quality's contribution, within fixed bounds, for the current Request only. It changes both the router's edge cost and the Loop score for that Request, so the Loops genuinely change. The weights themselves stay in code and are never edited from the UI, and the Reference Loop tests check them at the centre. This keeps the earlier decision that weights are fixed and tuned against Reference Loops, while giving the user Trail Router's immediate, tangible control.

The Request's settings (Distance, Activity, Leans, Heading, start) are remembered and restored next time.

## Considered options

- **Sliders that edit the weights.** Rejected: every Request would depend on where the sliders were last left, and the Reference Loop tests could no longer guard the weights.
- **No sliders (the earlier decision).** Rejected after seeing Trail Router's UI: instant, visible control over a few qualities makes comparing Loops far easier.

## Consequences

- Moving a Lean first **re-ranks the candidates already found** (instant, since every candidate's Breakdown is in memory), then a background search looks for better candidates under the new balance.
- **Hills** reverses the earlier rule that climbing is shown but never asked for.
- **Novelty** replaces the Surprise on/off switch. The 10% unfamiliar-ground target applies when Novelty is at or above its midpoint.
- Lean bounds are part of `ScoringWeights`, so a Lean can't push a Main Road into a Loop or override Access: the Main Road rule and Access are not Leans.
