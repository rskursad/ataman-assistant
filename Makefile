DOTNET   ?= $(HOME)/.dotnet/dotnet
CONFIG   ?= Debug
SHELL    := /bin/bash

PROJS := Ataman.Core/Ataman.Core.csproj \
         ataman-assistant/AtamanAssistant.csproj \
         ataman-assistant.Desktop/AtamanAssistant.Desktop.csproj \
         tools/SmokeTest/SmokeTest.csproj

# ──────────────────────────── Restore ────────────────────────────
.PHONY: restore
restore:
	@for p in $(PROJS); do $(DOTNET) restore $$p; done

# ─────────────────────── Config aliases ─────────────────────────
.PHONY: debug release
debug: CONFIG := Debug
debug: build

release: CONFIG := Release
release: build

# ──────────────────────────── Build ──────────────────────────────
.PHONY: build build-core build-desktop build-smoke
build: build-desktop

build-core:
	$(DOTNET) build Ataman.Core/Ataman.Core.csproj -c $(CONFIG) -nodeReuse:false

build-desktop: build-core
	$(DOTNET) build ataman-assistant.Desktop/AtamanAssistant.Desktop.csproj -c $(CONFIG) -nodeReuse:false

build-smoke:
	$(DOTNET) build tools/SmokeTest/SmokeTest.csproj -c $(CONFIG) -nodeReuse:false

# ──────────────────────────── Run ────────────────────────────────
.PHONY: run run-smoke
run: build-desktop
	$(DOTNET) run --project ataman-assistant.Desktop/AtamanAssistant.Desktop.csproj -c $(CONFIG)

run-smoke: build-smoke
	$(DOTNET) run --project tools/SmokeTest/SmokeTest.csproj -c $(CONFIG)

# ──────────────────────────── Android ────────────────────────────
.PHONY: android-build android-run
android-build:
	$(DOTNET) build ataman-assistant.Android/AtamanAssistant.Android.csproj -c $(CONFIG) -f net10.0-android

android-run:
	$(DOTNET) build ataman-assistant.Android/AtamanAssistant.Android.csproj -c $(CONFIG) -t:Run -nodeReuse:false

# ──────────────────────────── Clean ──────────────────────────────
.PHONY: clean
clean:
	@for p in $(PROJS); do $(DOTNET) clean $$p -c $(CONFIG); done
	@find . -type d \( -name obj -o -name bin \) -exec rm -rf {} + 2>/dev/null; true

# ──────────────────────────── Help ───────────────────────────────
.PHONY: help
help:
	@echo "Kullanım: make <hedef> [CONFIG=Release]"
	@echo ""
	@echo "Hedefler:"
	@echo "  debug          Debug modda derle (varsayılan)"
	@echo "  release        Release modda derle"
	@echo "  restore        NuGet paketlerini indir"
	@echo "  build          Desktop projesini derle (varsayılan)"
	@echo "  build-core     Sadece Ataman.Core'ı derle"
	@echo "  build-desktop  Desktop projesini derle"
	@echo "  build-smoke    SmokeTest'i derle"
	@echo "  run            Desktop uygulamasını çalıştır"
	@echo "  run-smoke      SmokeTest'i çalıştır"
	@echo "  android-build  Android APK derle"
	@echo "  android-run    Android'de çalıştır (USB)"
	@echo "  clean          Tüm build çıktılarını temizle"
	@echo "  help           Bu mesajı göster"
