# ----------------------------------------------------------------------------
# CodeBrix.Audio.Engine native backend (codebrix_miniaudio)
#
# Vendored from SoundFlow's Native/miniaudio-backend (LSXPrime/SoundFlow, MIT).
# Changes made for CodeBrix.Audio.Engine:
#   * LIBRARY_NAME "miniaudio" -> "codebrix_miniaudio" so the shipped native lib
#     never clashes with a stock miniaudio.* that a consuming app might also load.
#   * miniaudio.h is vendored in-repo at miniaudio-80cf7b2/ (mackron/miniaudio
#     @ 80cf7b2, v0.11.24) instead of a git submodule.
#   * Pruned to the platforms we ship (Windows / Linux / macOS). SoundFlow's
#     iOS / Android / Emscripten branches were removed.
#   * macOS guard also matches "Darwin" (CMake's default CMAKE_SYSTEM_NAME on
#     macOS) so a plain `cmake` configure works without -DCMAKE_SYSTEM_NAME=macOS.
# See THIRD-PARTY-NOTICES.txt for full provenance and license text.
# ----------------------------------------------------------------------------

cmake_minimum_required(VERSION 3.26)
project(codebrix_miniaudio C)

set(CMAKE_C_STANDARD 11)

include_directories(${CMAKE_CURRENT_SOURCE_DIR})

# Set base library name
set(LIBRARY_NAME "codebrix_miniaudio")

# Building Shared Library
add_library(${LIBRARY_NAME} SHARED
        library.c
        library.h
        miniaudio-80cf7b2/miniaudio.h)

# Platform-specific configurations
if (CMAKE_SYSTEM_NAME STREQUAL "Windows")
    add_definitions(-DMA_DLL)

    if (MSVC)
        add_definitions(-D_CRT_SECURE_NO_WARNINGS)
    endif()

    if (CMAKE_COMPILER_IS_GNUCC)
        set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -static-libgcc")

        target_link_libraries(${LIBRARY_NAME}
                -static
                gcc
                winpthread
                -dynamic
        )
    endif ()

    # Windows: codebrix_miniaudio.dll
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX ""
            SUFFIX ".dll"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -static-libgcc")

    target_link_libraries(${LIBRARY_NAME}
            dl
            pthread
            m
    )

    # Linux: libcodebrix_miniaudio.so
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX "lib"
            SUFFIX ".so"
    )

elseif (CMAKE_SYSTEM_NAME STREQUAL "Darwin" OR CMAKE_SYSTEM_NAME STREQUAL "macOS")
    # Define MA_NO_RUNTIME_LINKING for macOS builds
    add_definitions(-DMA_NO_RUNTIME_LINKING)

    # Explicitly link required frameworks as per miniaudio docs
    target_link_libraries(${LIBRARY_NAME}
            "-framework CoreFoundation"
            "-framework CoreAudio"
            "-framework AudioToolbox"
            "-lpthread"
            "-lm"
    )

    # macOS: libcodebrix_miniaudio.dylib
    set_target_properties(${LIBRARY_NAME} PROPERTIES
            PREFIX "lib"
            SUFFIX ".dylib"
            # Enable code signing for macOS (ad-hoc signing)
            CODE_SIGNING_REQUIRED TRUE
            CODE_SIGNING_ALLOWED TRUE
            CODE_SIGN_IDENTITY "-"
    )

endif ()
