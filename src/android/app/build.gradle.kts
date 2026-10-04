plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "com.dormhi.localshare"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.dormhi.localshare"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "0.1.0"
    }

    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)
}


// SDK-only executable protocol tests: no test-framework dependency or device.
tasks.register<JavaExec>("videoProtocolTest") {
    dependsOn("compileDebugUnitTestKotlin")
    classpath = files(provider { tasks.named("compileDebugUnitTestKotlin").get().outputs.files },
        provider { tasks.named("compileDebugKotlin").get().outputs.files },
        provider { configurations.getByName("debugUnitTestRuntimeClasspath") })
    mainClass.set("com.dormhi.localshare.VideoProtocolTestKt")
}
