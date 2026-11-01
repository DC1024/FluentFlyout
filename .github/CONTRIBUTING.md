# How to contribute
Any help towards improving FluentFlyout is greatly appreciated — whether it's new features, bug fixes, or translations!

## Developing
To start developing, load the repository into Visual Studio (or your IDE of choice), make sure that FluentFlyout is closed (because it's a singleton app), and start writing!
Make sure to create a new [fork](https://github.com/DC1024/FluentFlyout/fork) of this repository when starting. You can then edit any code in your fork (using branches if needed). 

Once you're ready with the updates, submit a [pull request](https://github.com/DC1024/FluentFlyout/compare) describing what's changed, and be sure to tag any issues (if applicable) your pull request is taking care of!

AI usage: AI tools may be used to assist with programming, provided that you design the solution yourself and carefully review the generated code for issues and redundancy. Heavy reliance on AI without clear knowledge of what's going on with your changes will not be merged, but having a strong understanding of it will.

## Translating
For the original app, translations go through [Weblate](https://hosted.weblate.org/engage/fluentflyout/) — that project belongs to upstream and this fork does not pull from it.

For this fork, edit the matching `FluentFlyoutWPF/Resources/Localization/Dictionary-*.xaml` and open a pull request. Two things to watch: keep the product name `FluentFlyout Lyrics` untranslated, and check whether the language uses possessive suffixes or vowel harmony (`FluentFlyout's`, `FluentFlyout'un`) before renaming — a blind replace produces broken grammar. The README's "Known differences" section explains why only English and Simplified Chinese were renamed.

The majority of FluentFlyout users (apart from English) come from China and India. If you could translate any of these two countries' languages, it would be greatly appreciated!
Apart from those countries, the following are other popular ones that would greatly benefit from your help: Turkey, Mexico, several South American countries, and Russia. Of course, feel free to help contribute to languages not listed as well!
