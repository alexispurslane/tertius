---
id: ter-3yy5
status: closed
deps: []
links: []
created: 2026-10-27T13:14:42Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, hair]
---
# Hair under the screen-space light: a curved or smooth-shaded card's own points block again

Found by the review of ter-qsfz (2026-10-27). The own-card rule of option A takes a hair receiver's card as the plane at right angles to its stored geometric normal, which is the interpolated vertex normal (material.glsl, sampleMaterial). On a smooth-shaded card strip that bends, such as MPFB's afro01 cards or geometry.py's cards (smooth below 35 degrees), that normal leans up to half the bend off the triangle's plane, so points of the receiver's own triangle lie far off the tested plane and block its sectors again; the darkening returns and changes along each segment. Even with the triangle's exact plane, the points of the card's next segment across a bend lie off that plane and block, so option A as a plane rule covers only flat cards.

Waiting for the user. Options seen so far: (1) the face normal from derivatives of the position in the G-buffer pass as a hair pixel's geometric normal (fixes points of the same triangle only; adds a precision term that grows with the resolution); (2) flat-shaded hair cards in the export (the strand then jumps at each bend, so the highlights show the segments); (3) the design states that hair cards must be flat (MPFB's curved cards break it); (4) a test by angle, a cone around the receiver's plane, which also skips nearby cards that lie almost in the plane; (5) a card identity in the G-buffer, which needs a new channel or target. The red test (a smooth-shaded card of two segments, turned 40 degrees) is in the ter-qsfz fixer's report.


## Notes

**2026-10-31T14:38:57Z**

2026-10-31: the user chose design A, the card number. A step belongs to a hair pixel's own card when its pixel carries the receiver's card number, at any bend; other cards still shade each other, and a curl that folds back over its own card never shades it. Import numbers the cards of a hair mesh (its connected strips of triangles) into a card number per triangle; the G-buffer writes a hair pixel's card number, mixed with its instance, into the 13 spare bits of the shading target (B keeps its light bit, A its model code); the sector pass, for a hair receiver, reads each step's card number in place of the plane test. Declined: design B, a cone of half-angle θ around the card plane (it also skips other cards within θ: sin²θ of the cosine-weighted light, 33 % at 35°), and options 1 to 3 of the ticket (partial, or flat cards only). Waits for a free builder slot.

**2026-10-31T15:32:08Z**

2026-10-31: workflow run 282e21f9-3482-4525-b6d4-220e846bbc24 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6), branch ter-3yy5-hair-card-number, base e4244a5c.

**2026-10-31T16:09:34Z**

2026-10-31, settled in the build: (1) The card number reaches the G-buffer as a vertex attribute, GpuVertex.Card, a uint at offset 48 of a 52-byte GPU vertex (location 4, R32_UINT); separate cards share no vertex, so each vertex carries its triangle's card. The public Vertex is unchanged: UploadQueue.AddMesh numbers every mesh's cards on the caller's thread (MeshCards.Number), because the renderer does not know a mesh's material. GpuDeformVertex grows to 96 bytes to carry the card into deformed copies, and deform.comp writes 13 words with floatBitsToUint, so the card bits store exactly. The region alignment becomes 64 vertices (3,328 bytes). gl_PrimitiveID was rejected: a fragment read needs the geometry or tessellation capability, and MoltenVK has no geometryShader. (2) A card is a strip joined by shared vertex positions, not only shared indices, so an exporter's split at a sharp bend or seam stays one card. (3) The mix: code = (card + 5063 * placedIndex) mod 8192, with placedIndex the instance's place among the frame's placed instances (GpuInstance.PlacedIndex, in the struct's padding), which every depth range and view of the instance shares. Collisions: none within one instance below 8,192 cards; neighbor instances' same card 3,129 codes apart; about 1 in 8,192 pairs of two instances' cards. (4) A non-hair pixel writes 0 into the 13 bits, and the sector pass reads NoCard for it, so it always blocks. (5) Removed with the plane test: onOwnCard, vertexSnap, NormalPrecision, OwnCard.Holds and SnapPerMeter, the subPixelPrecisionBits push constant (20 to 16 bytes) and the SubPixelPrecisionBits test hook. (6) The review scene hair-bent-cards and scripts/hair-bent-cards.json show five smooth-shaded cards that curl along their strands beside five flat ones. GPU cost, 1080p, 2 runs each: G-buffer 0.405/0.391 to 0.384/0.414 ms, sectors 1.426/1.389 to 1.389/1.481 ms, within run-to-run spread.

**2026-10-31T16:52:03Z**

2026-10-31, the fixes after the review: (1) A mesh's cards no longer travel as an array beside its vertices. StagedMesh.Of builds each GPU vertex with its card once, on the caller's thread; UploadQueue.Pending, GeometryStore.Stage and MeshDeform.Pack take the StagedMesh, so no caller pairs a card with a vertex by hand. (2) CardCode holds the C# twins of the shading target's layout (ModelOf, LightMarkOf beside Channels and Read), and no C# reader masks B or A by hand. (3) GpuInstance.From takes placedIndex as a required argument, and StepTexel.Card is required. The shadow pass and the impostor captures pass 0, because their draws write no card code. (4) PlacedIndex, not the entity's (EntityIndex, Part): the mix needs one dense number per frame, so that the places of neighbor instances give codes 3,129 apart; an entity index is sparse and the pair is two numbers, which would need a hash with no spacing. (5) The join by position is a decision: two strips that touch at one exact position are one card (MeshCardsTests, Known limits). MakeHuman's afro01 source mesh has no vertices that share a position, so its 342 strips stay 342 cards, far below 8,192; two of them are sheets of 378 quads each round the head, beside 340 single quads. (6) The step in gray on the turned cards of hair-bent-cards is the direct light of the most turned segment: with --screen-space-light off, the same step shows (38 to 31 in 8-bit gray, against 37 to 30 with it on), and the screen-space light changes no card pixel by more than 2 of 255.

**2026-10-31T17:19:46Z**

Orchestrator's pass before the merge (2026-10-31). Hand mutations, each built and run against its test class: (1) MeshCards.Number joins no triangles: killed by all 5 MeshCardsTests. (2) It joins no shared positions: killed by TwoStripsThatTouchAtOnePositionAreOneCard and TrianglesThatMeetOnlyAtVerticesOfOnePositionAreOneCard. (3) CardCode's instance step 0: killed by OneCardOfTwoInstancesHasTwoCodes and TheCardsOfNeighborInstancesLieFarApartInTheCodes. (4) The C# own-card test never matches (card + 1): killed by 3 SectorSearchTests. (5) occlusion_sectors.comp never skips the own card: killed by 4 HairRenderTests, the red test among them. (6) mesh.vert writes card 0 for every vertex: killed by all 35 HairRenderTests. (7) mesh.vert drops the instance from the code: killed by UnderTheScreenSpaceLightAHairCardDarkensAHairCardBehindIt. (8) deform.comp writes 0 as the card word: killed by ADeformedCopyCarriesTheCardNumbersOfItsMesh. (9) The G-buffer writes the code for every model: killed by TheGBufferWritesEachModelsCode. Coverage of the changed lines runs once for the whole epic ter-n7lw, before it closes (project rule: each epic).

**2026-10-31T17:31:55Z**

2026-10-31: merged into main (a7c101ce). tools/test-all.sh on the merged code: every module passed in Checked and Release, except one Release run of the physics test ContactAllocationTests.StepsOfAwakeBodiesInATurningFrameAllocateNothingOnAnyThread at a load of 34, which passed alone a minute later; filed as ter-a00h. The merge changes no physics code.
