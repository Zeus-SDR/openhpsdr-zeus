# native/miniaudio/CMakeLists.txt — Zeus miniaudio shim build
#
# Produces:
#   macOS   : libminiaudio.dylib  (arm64, x86_64) — CoreAudio backend
#   Linux   : libminiaudio.so     (x86_64, arm64) — ALSA + PulseAudio + JACK
#   Windows : miniaudio.dll       (x64, arm64)    — WASAPI
#
# Source is vendored upstream `miniaudio.h` (David Reid / mackron, v0.11.25,
# MIT-0 / public-domain) plus `zeus_miniaudio.c` which wraps it in the small
# stable C ABI the Zeus C# P/Invoke binds against. See README.md.

cmake_minimum_required(VERSION 3.20)
project(zeus_miniaudio
    VERSION 0.11.25
    DESCRIPTION "miniaudio audio I/O — Zeus desktop-mode RX sink + TX mic capture"
    LANGUAGES C
)

set(CMAKE_C_STANDARD 11)
# Hide everything by default; the Zeus ABI uses an explicit visibility
# attribute so only zeus_ma_* exports leak out. Keeps miniaudio's giant
# internal symbol table from polluting the .dylib.
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

add_library(miniaudio SHARED
    zeus_miniaudio.c
)

target_include_directories(miniaudio PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
)

# Per-platform linker needs.
if(APPLE)
    # miniaudio CoreAudio backend talks to AudioToolbox + CoreFoundation +
    # CoreAudio. AudioUnit isn't strictly needed for ma_device but is pulled
    # in by AudioToolbox transitively.
    find_library(COREAUDIO_LIB CoreAudio)
    find_library(AUDIOTOOLBOX_LIB AudioToolbox)
    find_library(AUDIOUNIT_LIB AudioUnit)
    find_library(COREFOUNDATION_LIB CoreFoundation)
    target_link_libraries(miniaudio PRIVATE
        ${COREAUDIO_LIB} ${AUDIOTOOLBOX_LIB} ${AUDIOUNIT_LIB} ${COREFOUNDATION_LIB}
    )
elseif(UNIX)
    # Linux: miniaudio loads ALSA / PulseAudio / JACK dynamically at runtime
    # via dlopen; only -ldl + -lpthread + -lm are required at link time.
    find_package(Threads REQUIRED)
    target_link_libraries(miniaudio PRIVATE Threads::Threads dl m)
elseif(WIN32)
    # WASAPI is loaded dynamically; no extra link libs required for the
    # default backend.
endif()

# Output filename: libminiaudio.{dylib,so} on UNIX, miniaudio.dll on Windows.
set_target_properties(miniaudio PROPERTIES
    OUTPUT_NAME "miniaudio"
    PREFIX "${CMAKE_SHARED_LIBRARY_PREFIX}"
)
