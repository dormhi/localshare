buildscript {
    dependencies {
        // AGP 9.4 supplies built-in Kotlin support; keep its KGP runtime aligned
        // with the Compose compiler plugin version selected in the version catalog.
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.10")
    }
}

plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.kotlin.compose) apply false
}
