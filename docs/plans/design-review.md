# Design review

Ticket: `ter-tanj`.

## Goal

Find the design problems in every module before more code builds on them. The review uses the software-design skill, the user's own design philosophy. It merges Ousterhout's deep modules and information hiding with Muratori's rule of two. It sorts every piece of a design into a boundary, a flow or a shared abstraction.

## How

1. Reviewers look at each module from several angles: correctness, determinism, thread safety, performance and interface design.
2. Independent skeptics try to refute each finding. Only the findings that survive go to the user.
3. Each accepted finding gets a failing test first, then the fix.

## Known items

The known items are the child tickets of `ter-tanj`. Run `tk show ter-tanj` to list them.

## Report

For each finding: the module, the problem, the evidence, the skeptics' verdict and the proposed fix.
