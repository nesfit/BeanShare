// Bridges Html5Qrcode camera library with Blazor SignalR callbacks
window.beanshareQrScanner = {
    _instance: null,
    _alreadyScanned: false,

    _looksLikeBeanShareCode: function (text) {
        if (!text || text.length < 5) {
            return false;
        }
        // v2: URL containing /scan/ followed by a GUID-like pattern
        if (text.indexOf('/scan/') !== -1) {
            return true;
        }
        // v1: JSON with our version marker
        if (text.charAt(0) === '{' && text.indexOf('"v"') !== -1) {
            return true;
        }
        return false;
    },

    start: async function (elementId, dotnetHelper) {
        try {
            if (this._instance) {
                await this.stop();
            }

            this._alreadyScanned = false;
            this._instance = new Html5Qrcode(elementId);

            var self = this;
            await this._instance.start(
                { facingMode: 'environment' },
                { fps: 8, qrbox: { width: 250, height: 250 }, aspectRatio: 1.0 },
                function (decodedText) {
                    if (self._alreadyScanned) {
                        return;
                    }
                    if (self._looksLikeBeanShareCode(decodedText)) {
                        self._alreadyScanned = true;
                        dotnetHelper.invokeMethodAsync('OnQrCodeScanned', decodedText);
                    }
                },
                function () {
                }
            );
            return true;
        } catch (err) {
            console.error('[BeanShare] Camera init failed:', err.message || err);
            return false;
        }
    },

    // Scans 1D product barcodes (EAN/UPC) — no BeanShare-payload filter; returns the raw code.
    startBarcode: async function (elementId, dotnetHelper) {
        try {
            if (this._instance) {
                await this.stop();
            }

            this._alreadyScanned = false;

            var formats;
            if (typeof Html5QrcodeSupportedFormats !== 'undefined') {
                formats = [
                    Html5QrcodeSupportedFormats.EAN_13,
                    Html5QrcodeSupportedFormats.EAN_8,
                    Html5QrcodeSupportedFormats.UPC_A,
                    Html5QrcodeSupportedFormats.UPC_E,
                    Html5QrcodeSupportedFormats.CODE_128,
                    Html5QrcodeSupportedFormats.QR_CODE
                ];
            }

            this._instance = formats
                ? new Html5Qrcode(elementId, { formatsToSupport: formats, verbose: false })
                : new Html5Qrcode(elementId);

            var self = this;
            await this._instance.start(
                { facingMode: 'environment' },
                { fps: 10, qrbox: { width: 280, height: 160 }, aspectRatio: 1.777 },
                function (decodedText) {
                    if (self._alreadyScanned) {
                        return;
                    }
                    self._alreadyScanned = true;
                    dotnetHelper.invokeMethodAsync('OnBarcodeScanned', decodedText);
                },
                function () {
                }
            );
            return true;
        } catch (err) {
            console.error('[BeanShare] Barcode camera init failed:', err.message || err);
            return false;
        }
    },

    stop: async function () {
        try {
            if (this._instance) {
                var state = this._instance.getState();
                if (state === Html5QrcodeScannerState.SCANNING ||
                    state === Html5QrcodeScannerState.PAUSED) {
                    await this._instance.stop();
                }
                this._instance.clear();
                this._instance = null;
            }
        } catch (err) {
            console.error('[BeanShare] Scanner cleanup error:', err.message || err);
            this._instance = null;
        }
    },

    scanFile: async function (fileInputId, dotnetHelper) {
        var tempContainer = null;
        try {
            var input = document.getElementById(fileInputId);
            if (!input || !input.files || input.files.length === 0) {
                return;
            }

            var imageFile = input.files[0];
            var containerId = 'bs-qr-file-' + Date.now();
            tempContainer = document.createElement('div');
            tempContainer.id = containerId;
            tempContainer.style.display = 'none';
            document.body.appendChild(tempContainer);

            var fileScanner = new Html5Qrcode(containerId);
            try {
                var decoded = await fileScanner.scanFile(imageFile, false);
                if (this._looksLikeBeanShareCode(decoded)) {
                    dotnetHelper.invokeMethodAsync('OnQrCodeScanned', decoded);
                } else {
                    dotnetHelper.invokeMethodAsync('OnQrScanError',
                        'Not a BeanShare QR code.');
                }
            } finally {
                fileScanner.clear();
            }
        } catch (err) {
            console.error('[BeanShare] Image scan failed:', err.message || err);
            dotnetHelper.invokeMethodAsync('OnQrScanError',
                'No QR code found in image.');
        } finally {
            if (tempContainer) {
                tempContainer.remove();
            }
        }
    }
};
