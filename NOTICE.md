# Licensing and attribution

Copyright (c) 2026 pigeonIabs.

Cutback and its original source are distributed under the GNU Affero General Public License, version 3 only. The complete license text is supplied in LICENSE.

## Runtime linking permission

As an additional permission under section 7 of GNU AGPL version 3, the copyright holder permits linking or combining the Cutback code they own with the separately distributed Beat Saber game assemblies and runtime mod dependencies documented in README.md, and conveying the resulting Cutback plugin. The licenses and distribution terms of those separate components remain applicable. Corresponding Cutback source remains available under GNU AGPL version 3.

This permission applies to the Cutback code owned by the copyright holder. Upstream components retain their own licenses.

## BeatLeader

LocalReplayEncoder.cs adapts BeatLeader's BSOR encoder. ReplayMetadata.cs adapts BeatLeader's metadata mapping. Both retain attribution to the upstream MIT-licensed source at commit 265424aa1dcd2ba205ccc78238354de6c52f4d59.

The upstream copyright and MIT terms are reproduced in ThirdParty/BeatLeader-LICENSE. Cutback's modifications to these files were made in 2026.

BeatLeader also supplies the recording and playback engine as an installed runtime dependency.

## PracticePlugin

PracticePlugin supplies optional live practice controls through its separately installed plugin. Its MIT license is reproduced in ThirdParty/PracticePlugin-LICENSE for attribution.

## Source availability

The source for each released Cutback binary is available through its matching release tag at https://github.com/pigeonIabs/Cutback.
