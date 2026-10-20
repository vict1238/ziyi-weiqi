These are neural nets for KataGo's third major run, which took place December through June 2020 (run id g170).

The run is complete! While it almost certainly could reach much stronger, due to some limitations on resources to train it, the run is stopped for now with some final hopefully very strong nets. It was a great run, and we hope to continue at some point with a community-distributed training run.

https://github.com/lightvector/KataGo

All of these nets support all the fancy new features for KataGo v1.3 and later - Japanese rules, true lead estimation, handicap game "PDA" aggression parameter, etc, as well as all of KataGo's older features in earlier versions - multiple board sizes, variable komi, ownership and score, etc.

*** If you just want the strongest nets to use for play or analysis, use these: ***
g170-b40c256x2-s5095420928-d1229425124    ("g170 40 block d1229M")
g170e-b20c256x2-s5303129600-d1228401921   ("g170e 20 block d1228M")

==== Full training net history =====================================================
These are mostly of interest to people doing more detailed stats or tests on the training run.

selfplayhistory - A directory containing the full history of neural nets used for self-play.

extrahistory - A directory containing the full history of neural nets that were off the main line for self-play, including the various extended-training nets of various sizes.

Various selected nets are also available, as documented below.

==== Selected semi-Zero selfplay nets ==============================================================

All of these nets are "semi-zero" in the sense that they make use of some non-"zero" input features (e.g. liberties and ladders) and output features (e.g. ownership prediction), but there is no outside data, and it is still entirely up to the net to learn on its own how to use those features or to ignore them in different situations.

"s" indicates training steps, "d" indicates number of selfplay data rows at that point in the run.

These are each the last (and either strongest or close to strongest) neural net of smaller sizes that KataGo used for self-play, before switching to larger sizes:

g170-b6c96-s175395328-d26788732  - 6 block x 96 channel      (~0.75 days)  ("g170 6 block d26M")
g170-b10c128-s197428736-d67404019 - 10 block x 128 channel   (~1.75 days)  ("g170 10 block d67M")
g170-b15c192-s497233664-d149638345 - 15 block x 192 channel  (~7.5 days)   ("g170 15 block d149M")

These are successively stronger 20-block KataGo nets. The additional "x2" in the name indicates that this size is being trained with 2 GPUs for gradients instead of just 1:

g170-b20c256x2-s668214784-d222255714 - 20 block x 256 channel (~15.5 days)   ("g170 20 block d222M")
g170-b20c256x2-s1039565568-d285739972 - 20 block x 256 channel (~21 days)    ("g170 20 block d285M")
g170-b20c256x2-s1420141824-d350969033 - 20 block x 256 channel (~27.5 days)  ("g170 20 block d350M")
g170-b20c256x2-s1913382912-d435450331 - 20 block x 256 channel (~35.5 days)  ("g170 20 block d435M")
g170-b20c256x2-s2107843328-d468617949 - 20 block x 256 channel (~38.5 days)  ("g170 20 block d468M")

And at this point, KataGo switched to using 30-block and 40-block nets for self-play (but the 20-block net also continued training, see "Extended training nets" below):

g170-b30c320x2-s1287828224-d525929064 - 30 block x 320 channel (~47.5 days)  ("g170 30 block d525M")
g170-b40c256x2-s1349368064-d524332537 - 40 block x 256 channel (~47 days)    ("g170 40 block d524M")
g170-b30c320x2-s1840604672-d633482024 - 30 block x 320 channel (~64.5 days)  ("g170 30 block d633M")
g170-b40c256x2-s1929311744-d633132024 - 40 block x 256 channel (~64.5 days)  ("g170 40 block d633M")
g170-b30c320x2-s2271129088-d716970897 - 30 block x 320 channel (~78 days)    ("g170 30 block d716M")
g170-b40c256x2-s2383550464-d716628997 - 40 block x 256 channel (~78 days)    ("g170 40 block d716M")
g170-b30c320x2-s2846858752-d829865719 - 30 block x 320 channel (~96 days)    ("g170 30 block d829M")
g170-b40c256x2-s2990766336-d830712531 - 40 block x 256 channel (~96 days)    ("g170 40 block d830M")
g170-b30c320x2-s3530176512-d968463914 - 30 block x 320 channel (~117 days)   ("g170 30 block d968M")
g170-b40c256x2-s3708042240-d967973220 - 40 block x 256 channel (~117 days)   ("g170 40 block d967M")

In addition to supporting lead estimation and various rulesets, and strong handicap game play, the last few nets here should be quite a bit stronger than Leela Zero - anywhere from perhaps 100 to 400 Elo, depending on hardware and match settings and parameters. As of June 2020, this makes KataGo's using these 117-day nets possibly the strongest open source bot available. Using these same nets KataGo also recently topped the 9x9 CGOS ladder, and with no serious competitors on 13x13, is possibly also the strongest available bot on both 9x9 and 13x13 as well.

==== Extended training nets =====================================================

These are continuations of the above neural nets of a given size, where even after KataGo switched to larger sizes, the smaller size continued training on the data from the larger sizes. Some are *much* stronger than the last selfplay net of that size.

g170e-b10c128-s1141046784-d204142634 - 10 block x 128 channel    ("g170e 10 block d204M")
g170e-b15c192-s1305382144-d335919935 - 15 block x 192 channel    ("g170e 15 block d335M")
g170e-b15c192-s1672170752-d466197061 - 15 block x 192 channel    ("g170e 15 block d455M")
g170e-b20c256x2-s2430231552-d525879064 - 20 block x 256 channel  ("g170e 20 block d525M")
g170e-b20c256x2-s2971705856-d633407024 - 20 block x 256 channel  ("g170e 20 block d633M")
g170e-b20c256x2-s3354994176-d716845198 - 20 block x 256 channel  ("g170e 20 block d716M")
g170e-b20c256x2-s3761649408-d809581368 - 20 block x 256 channel  ("g170e 20 block d809M")
g170e-b20c256x2-s4384473088-d968438914 - 20 block x 256 channel  ("g170e 20 block d968M")

==== External-data-biased nets =====================================================

These are the final segment of the run where KataGo very experimentally started seeing what the effect of adding external data might be in certain particular ways (although there were many other ways that weren't explored too!).

These three nets trained 5% of self-play games starting from random sampled positions from pro and high-dan human games including from midgames and endgames and also some number of starting positions sampled from Leela Zero gating games. For about the last quarter of that time, they also trained another 5% of self-play games starting specifically from "hint" positions mined from those games where the external move was evaluated to be an unexpectedly good move. But finishing the entire game still as normal, so the surprising move learning would still only be a small fraction of the data even from that game. So a total of 10% games of external data for the last quarter.
g170-b30c320x2-s3910534144-d1045712926  ("g170 30 block d1045M")
g170-b40c256x2-s4120339456-d1045882697  ("g170 40 block d1045M")
g170e-b20c256x2-s4667204096-d1045479207 ("g170e 20 block d1045M")

These three nets continued 5% random external game starting positions + 5% hint position training, except that the 5% random external game starting positions was replaced with 4% random external game starting positions and 1% starting positions with random variations of Mi Yuting's flying dagger joseki, a specific bot-discovered joseki that is immensely complex with a huge number of variations and that even top AIs do not always play correctly.
g170-b30c320x2-s4054753280-d1074791858    ("g170 30 block d1074M")
g170-b40c256x2-s4272960512-d1074391043    ("g170 40 block d1074M")
g170e-b20c256x2-s4774523136-d1074391043   ("g170e 20 block d1074M")

These three nets continued training but with 5% external game hint positions replaced with 4% external game hint positions and 1% flying dagger joseki hint positions.
g170-b30c320x2-s4141693952-d1091071549    ("g170 30 block d1091M")
g170-b40c256x2-s4368856832-d1091190099    ("g170 40 block d1091M")
g170e-b20c256x2-s4842585088-d1091433838   ("g170e 20 block d1091M")

And these three nets continued training for much longer on a blend of external data and hint positions, sometimes varying the proportion a little,
as well as some limited and sparse training on a small number of specific known blind spots.
g170-b30c320x2-s4432082944-d1149895217    ("g170 30 block d1149M")
g170-b40c256x2-s4679779328-d1149909226    ("g170 40 block d1149M")
g170e-b20c256x2-s5055114240-d1149032340   ("g170e 20 block d1149M")

==== Final nets =====================================================

These nets were produced after a large learning rate drop (lr-scale 0.35 -> lr-scale 0.1 -> lr-scale 0.05) plus continuing more external data and hint position training. At least as measured by self-play, the final 30 and 40 block nets are *much* stronger than any of the previous above nets, perhaps somewhere between 200 and 250 Elo better at some moderate fixed visits settings, while the the 20 block extended training net also made some gains, perhaps between 50 and 100 Elo.

lr-scale 0.1 and about a week of training:
g170-b30c320x2-s4574191104-d1178681586    ("g170 30 block d1178M")
g170-b40c256x2-s4833666560-d1179059206    ("g170 40 block d1179M")
g170e-b20c256x2-s5132547840-d1177695086   ("g170e 20 block d1177M")

lr-scale 0.05 plus another couple weeks of training:
g170-b30c320x2-s4824661760-d1229536699    ("g170 30 block d1229M")
g170-b40c256x2-s5095420928-d1229425124    ("g170 40 block d1229M")
g170e-b20c256x2-s5303129600-d1228401921   ("g170e 20 block d1228M")

And a very large neural net size that was never used for self-play, but that was a candidate for the next size of neural net to use:
g170e-b40c384x2-s2348692992-d1229892979   ("g170e 40 block 384 channel d1229M")

==== Zip File Contents ==================================================

*/model.txt.gz OR */model.bin.gz - This is the neural net model file that KataGo's engine uses as an input for everything. No need to unzip down further to the .txt or .bin file itself, just pass the model.txt.gz or model.bin.gz file directly. 

The .bin.gz file is a newer partially binary format that KataGo v1.3.3+ can load much more quickly.

The other two more of interest for developers:

*/model.config.json - json file describing model architecture parameters. Generally this file is an argument to all the python scripts used to build the model with the right number of blocks and channels.
*/saved_model - A directory containing a dump of the model weights in Tensorflow's format. Note that g170 uses stochastic weight averaging ("SWA") when dumping these values, which saves from a model constructed with a tf.name_scope("swa_model"), so you will generally need to pass -name-scope 'swa_model' to any python scripts to build their models under this name scope to be able to load these values in if you want to play around with the python scripts.

==== BIN.GZ Files ==================================================
For some of the older models in this run, which used an older and slower-to-load ".txt.gz" format, a separate .bin.gz file is provided for download in a newer model format that KataGo v1.3.3+ can load much more quickly.

Only versions of KataGo from v1.3.3 and later can load these files, older KataGo verisions do not support this format.


==== Strength ==================================================

Below is an APPROXIMATE relative Elo strength of these nets, using fixed amount of search (1200 visits), on 19x19 even games, in a playing pool with a lot of different KataGo versions, using some particular configuration.

Based on some simple statistics, roughly the error on all of these could easily be as large as +/- 40 points.

Also, the actual difference you find in practice may vary from these due to differences in your own configuration of KataGo's parameters, your batch size and threads, if using a different sized cap or a playout cap instead, or what opponents you test against.

Note that the smaller nets are much faster to run than the big nets, so if you are interested in equal wall clock time strength, the smaller nets will be relatively stronger than they appear in this table.


Neural net                                    Elo
g170-b6c96-s175395328-d26788732             -1187
g170-b10c128-s197428736-d67404019            -280
g170e-b10c128-s1141046784-d204142634          300
g170-b15c192-s497233664-d149638345            515
g170e-b15c192-s1305382144-d335919935          880
g170e-b15c192-s1672170752-d466197061          937
g170-b20c256x2-s668214784-d222255714          965
g170-b20c256x2-s1039565568-d285739972        1076
g170-b20c256x2-s1420141824-d350969033        1176
g170-b20c256x2-s1913382912-d435450331        1275
g170-b20c256x2-s2107843328-d468617949        1296
g170e-b20c256x2-s2430231552-d525879064       1345
g170-b30c320x2-s1287828224-d525929064        1414
g170-b40c256x2-s1349368064-d524332537        1411
g170e-b20c256x2-s2971705856-d633407024       1410
g170-b30c320x2-s1840604672-d633482024        1522
g170-b40c256x2-s1929311744-d633132024        1507
g170e-b20c256x2-s3354994176-d716845198       1462
g170-b30c320x2-s2271129088-d716970897        1559
g170-b40c256x2-s2383550464-d716628997        1556
g170e-b20c256x2-s3761649408-d809581368       1507
g170-b30c320x2-s2846858752-d829865719        1615
g170-b40c256x2-s2990766336-d830712531        1624
g170e-b20c256x2-s4384473088-d968438914       1530
g170-b30c320x2-s3530176512-d968463914        1644
g170-b40c256x2-s3708042240-d967973220        1683

External-data-biased nets:
g170-b30c320x2-s3910534144-d1045712926       1651
g170-b40c256x2-s4120339456-d1045882697       1698
g170e-b20c256x2-s4667204096-d1045479207      1561

g170-b30c320x2-s4141693952-d1091071549       1653
g170-b40c256x2-s4368856832-d1091190099       1680
g170e-b20c256x2-s4842585088-d1091433838      1547

g170-b30c320x2-s4432082944-d1149895217       1648
g170-b40c256x2-s4679779328-d1149909226       1690
g170e-b20c256x2-s5055114240-d1149032340      1539

Final LR-drop nets:
g170-b30c320x2-s4574191104-d1178681586       1759
g170-b40c256x2-s4833666560-d1179059206       1788
g170e-b20c256x2-s5132547840-d1177695086      1577

g170-b30c320x2-s4824661760-d1229536699       1908
g170-b40c256x2-s5095420928-d1229425124       1919
g170e-b20c256x2-s5303129600-d1228401921      1645


And for comparison to the old 2019 June official run:
g104-b6c96-s97778688-d23397744              -1149
g104-b10c128-s110887936-d54937276            -479
g104-b15c192-s297383936-d140330251            327
g104-b20c256-s447913472-d241840887            909
                          

The very fat net g170e-b40c384x2-s2348692992-d1229892979 has never been tested, there is a good chance it is stronger per-playout than any of the above nets but will be slow to run.


