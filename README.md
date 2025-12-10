# Splats

A GPU based splat management system, that handles the painting and editing of
splats for you. Splats is intended for top-down games that need to support an
infinite world.

At some point, I'd like to add better support for finite worlds.

This was developed in part as a final project for CMPM 169 @ UCSC, Fall '25. As
a result, there's some non-critical information in this README.

## An Artistic Statement

**_Splats_** is as much a technical project as it is an artistic project, a
backbone system used to make an expected, often neglected, game element more
exciting by giving it the potential to have a more direct impact on gameplay
without requiring too much inline logic. My game has a flat MS-Paint like art
style, though the lighting effects are more advanced, think caustics. I intended
to enable both more sophisticated rendering for splat effects, while opening it
up to expanded gameplay possibilities and emergent systems without a heavy
performance cost. **_Splats_** is my attempt at creating a flexible foundation
for visual aesthetics and gameplay dynamics that I can use for every top-down
game I make.

## Instillation & Getting Started

You can install this package directly using the
[Unity Package Manager](https://docs.unity3d.com/Manual/upm-ui-giturl.html), by
adding a git url.

```
https://github.com/Spebby/splats.git
```

Or, if you prefer, you can clone/download the repository directly inside your
project's `Assets` or `Packages` directory, though you will have to manually
resolve the UpdateManager dependency if you do this.

Once Splats is in your project, you can initilise it in a scene using the
`SplatBootstrap` MonoBehaviour, or you can write your own if you need more fine
control. Once the system has been initilised, you can interface with it globally
using the `SplatsMan` class.

### Spawning

Spawn a splat at a given world position based on input parameters. You must
specify which splat you'd like to spawn (Either by numeric ID or string ID).
Optionally, you can specify how many splats you'd like to spawn, the lifetime,
and other parameters like rotation, scale, shear, or a linear transformation
matrix, if you so wish.

### Queries

You can query the SplatMap using worldspace coordinates in order to extract
information, like what splat is in this position. You could use this in
combination with a gameplay system, like a debuff manager, to apply debuffs to
enemies that stand in certain splats.

For simplicity, enemies are considered to be standing in only 1 splat at a time,
which will be the mode of the region queried on the SplatMap.

### Editing

Not currently supported, but planned! You will be able to do fine tweaks to the
SplatMap from C#, such as replacing pixels of a splat with another splats, or
cutting/filling holes.

### Shaders

Both the unprocessed render textures and what the camera sees, are exposed as
globals to all shaders. The Chunk render textures can be accessed as `_Chunks`,
and the Camera's POV can be accessed as `_SplatMap`.

If you intend to write your own shaders, you should be aware that each Chunk's
texture is a 16bit RG-Half texture. For a given pixel, the R channel encodes the
splat ID, and the G channel encodes the lifetime. Pixels with negative lifetime
are interpreted as having no lifetime and will never decay.

If you want to write a special fragement shader for your splat type, you should
only operate on pixels with R-values that match the ID you are targeting.

## Postmortem

I had three main motivations for this project.

1. My game gets super laggy late game with tons of splats on screen. This isn't
   the only bottleneck, but it's a pretty major one.
2. I wanted a flexible interface for interactions between what was previously
   only a visual element, and gameplay systems like buffs, as well as the
   potential for more complex systems like fire.
3. I wanted to do a more complicated project based on compute shaders to push my
   understanding further.

Of them, I feel I successfully addressed the first and the last. Things run
better, I learned a hell of a lot, but the system is still pretty brittle, and I
didn't have as much time to work on some of the related gameplay systems I was
building this system for. As a result, this system has been a bit difficult to
integrate into my game, as the time working on the API was largely informed by
speculation, not actual use.

This project was developed relatively quickly and with a fairly long ideation
phase, so there was a lot planned that was cut for the first version. When I
started this project I was only really experienced with writing shaders &
compute shaders for isolated "blocks". For example, my
[Slime Mold](https://github.com/Spebby/Wang-Slime) simulation was fairly easy to
write, because the entire "world" was a single render texture. This project
proved to be a lot harder to reason about and implement due to mainly bounds
checking.

My biggest hiccup with the core system was spawning logic. If a splat is spawned
on a boundary and bleeds into multiple chunks, then there has to be a dispatch
for every chunk bled onto. I had to use AABB's to find out what parts of the
splat overlapped which parts of a chunk in order to paint in those chunks.
Combined with skewing, this became a headache.

So, why'd I skew? Skewing is especially useful to get extra use out of a limited
number of art assets, and is relatively cheap computationally. I had to read up
on some linear algebra for this, I'm a bit rusty, and the whole translating
to-and-from pixel space, local UV space, and into the skewed UV space was more
fun on paper than it was in my head or in my code.

These factors would lead to my first big compromise: axing random splat
generation. This is technically a lot simpler than it sounds. Take a texture,
skew and rotate it a bit, and then paint it, and do this a couple of times to
create a compound shape, getting even more out of a limited sprite set. So, why
didn't I do it? Well, I got stuck on the concept of creating a singular splat
which would be painted in one go. In hindsight, just running an extra dispatch
or two really wouldn't have been a big deal...

The next big compromise I would make was cutting the fire spreading system. I
was intending to make it a plugin to this plugin. Ideally, several systems could
interface with the SplatMan, rather than having these features inbuilt directly.
This was one of the first things I worked on, even before the GPU painting, it
got me to think about the required elements for my API. Though, as previously
discussed, the GPU portion took a lot longer than I was expecting for it to, and
I had to put this aside for another day. You can find my sketch for it in the
FirePlugin directory, though it is incomplete. If you check back at some point,
it'll probably be done. I do need it eventually haha.

Despite all those compromises and hiccups, some work wasn't too bad. The AABB
work I did for spawning proved reusable for CameraStitching. On
CameraStitching... I am still unsure this is the best way to do what I am doing,
but it reduced the amount of data I had to pass to various shaders, so it was
maybe the right decision after all. It was a bit of a headache to get working
right, since translating between world chunk PPU, world units and then to camera
space has been annoying, and I ultimately decided to side step it early on. I
will have to revisit it, but it wasn't worth the effort.

I'd like to focus a bit more on the positives, I learned a hell of a lot about
toying with UV coordinates, and got to apply some of the parallelism concepts I
learned awhile back, like barriers and groupsync memory. I was quite pleased
with the API I ended up making. There's definitely a lot of improvements to make
it usable for people who aren't me, but I think it works pretty well all things
considered, and definitely is enough for my game. Structuring it as an external
package has helped with another project, refactoring and cleaning up my game.

Even if a project "isn't done" like this one, I find it useful to reflect on the
last chunk of development and consider what I may have done differently. For
one, I wish I had started with just "painting a splat on a render texture",
instead of jumping into the chunks head first. Would have made my life a lot,
lot easier and saved me a couple precious days.

## Credits

gilzoide's [Update Manager](github.com/gilzoide/unity-update-manager)

kodai100's
[Matrix2x2](https://gist.github.com/kodai100/833a726cfd81a84cf3d116f41564bda6)

This project would not have been possible without
[Jasper Flick's](https://catlikecoding.com/),
[MinionsArt](https://minionsart.github.io/tutorials/) or
[Ben Cloward's](https://www.youtube.com/@BenCloward) awesome tutorials. Even if
what they were doing didn't directly relate, it often gave me a lot of
perspective or helped jog my mind.

Similarly, [Acerola](https://www.youtube.com/@Acerola_t) and
[Sebastian Lague](https://www.youtube.com/@SebastianLague) both got me into
shader writing through their videos. I would not have ever attempted this
project without them.
