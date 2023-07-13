# Gracious

Gracious (named after [Craig / Giarc](https://craig.chat)) is a multi-track voice channel recording bot for Discord that has some additional features that I wanted to add for my own purposes.

The history of this bot:
- I used to use a self-hosted Craig instance, alongside the public Craig and Giarc instances, to record a weekly event that lasts for approximately 3 hours.  I would also start recording my desktop at the same time using a self-host of Ennuicastr.
- Almost every week, at least one of the three instances (usually one of the public ones) would drop out of the channel partway through, and I would have to rely either on one of the other instances, or my desktop recording (without the multi-track split, which has been useful almost every time because of the nature of the event).
- Eventually I just got fed up with how unreliable Craig was, and I wrote an utterly trivial bot that would just join the voice channel, dump every relevant voice packet it received to a stream on disk (along with a high-precision timestamp of when it received it, with only the processing that DSharpPlus did to decode pcm_s16le), and then figure it out later.
- Running that alongside all the other bots for the next session, the others had problems as-usual, but this one was perfectly fine the whole time.  This led me to believe that the complexity that Craig had grown over the years (presumably, in response to actual problems that Discord put in front of it at some point in time) was likely either no longer needed, or redundant with what DSharpPlus was already doing at that time.  Giarc was out, though I did keep Craig and my self-host around for a bit longer.
- After a few more sessions where Craig and my self-host would crap out, I decided to just focus on making this bot glue together more of the things that I wanted from it (which was just "build ffmpeg commands and run them", because ffmpeg is the solution to everything) and drop the other two.
- With only one or two exceptions (nothing to do with this code, its dependencies, or Discord), this bot has successfully helped me out on every weekly session for the past year (at the time of writing this README).  I've been holding back on publishing the source code because the amount of actual "inventiveness" that I've put into it might put it into a bit of a gray area with a contract that I've signed with my employer.  After talking with them, they confirmed that they want nothing to do with this, so I have the full rights to redistribute this.

## License

I have published this under `AGPL-3.0-or-later`.

To summarize what that means (this is a non-binding summary, read the license for the actual terms), anyone who stumbled their way over here is allowed to use this code and run it for any purpose that they want, and they can even make changes to it and run that changed version, with the main condition being that **if** they distribute it to anyone else (even if that "distribution" is just letting someone else interact with it over a network!) then they **need** to offer the source code to that other person too.  If you're just running it without changes, then it's enough to just point them back to my version (assuming I'm still publishing it somewhere, which I expect to do).