/*
 * Communication Game System - Microcontroller Firmware
 * Target: ATmega32 @ 8 MHz (Proteus simulation)
 * UART: 9600 baud, 8N1, COBS-framed binary packets with CRC-8
 *
 * Packet format (before COBS):
 *   [TYPE(1)] [SEQ(1)] [LEN(1)] [PAYLOAD(0..N)] [CRC8(1)]
 * On wire: COBS-encoded packet followed by 0x00 delimiter.
 */

#define F_CPU 8000000UL
#include <avr/io.h>
#include <avr/interrupt.h>
#include <util/delay.h>
#include <string.h>

/* --- Packet types (must match server UartPacketType enum) --- */
#define PKT_HELLO        0x01
#define PKT_WELCOME      0x02
#define PKT_READY        0x03
#define PKT_START_STREAM 0x04
#define PKT_STOP_STREAM  0x05
#define PKT_DATA         0x06
#define PKT_PING         0x07
#define PKT_PONG         0x08
#define PKT_ERROR        0x09
#define PKT_ACK          0x0A

/* --- MCU states --- */
#define STATE_DISCONNECTED 0
#define STATE_HANDSHAKING  1
#define STATE_CONNECTED    2
#define STATE_STREAMING    3

/* --- Buffers --- */
#define MAX_RAW   16
#define MAX_COBS  20
#define COBS_DELIM 0x00

static uint8_t tx_seq = 0;
static uint8_t state = STATE_DISCONNECTED;
static uint8_t rx_buf[MAX_COBS];
static uint8_t rx_len = 0;

/*
 * Interrupt-driven UART receive ring buffer.
 *
 * The ATmega32 USART has only a 2-byte hardware receive buffer (UDR + the
 * shift register). The main loop uses blocking _delay_ms() calls, during which
 * incoming bytes are NOT polled. A multi-byte server frame (e.g. WELCOME) that
 * arrives during a delay overruns the hardware buffer and is lost, so the
 * handshake never completes and the MCU keeps resending HELLO forever.
 *
 * Capturing every received byte in an ISR-backed ring buffer decouples
 * reception from the main-loop timing and fixes the stuck-handshake bug.
 */
#define RX_RING_SIZE 64
static volatile uint8_t rx_ring[RX_RING_SIZE];
static volatile uint8_t rx_ring_head = 0; /* written by ISR */
static volatile uint8_t rx_ring_tail = 0; /* read by main loop */

/* ==================== Hardware ==================== */

void uart_init(void)
{
    UBRRH = 0;
    UBRRL = 51; /* 9600 @ 8 MHz */
    /* Enable TX, RX, and the RX-complete interrupt (RXCIE). */
    UCSRB = (1 << TXEN) | (1 << RXEN) | (1 << RXCIE);
    UCSRC = (1 << URSEL) | (1 << UCSZ1) | (1 << UCSZ0); /* 8N1 */
}

void uart_tx(uint8_t c)
{
    while (!(UCSRA & (1 << UDRE)));
    UDR = c;
}

/* USART receive-complete ISR: store every incoming byte in the ring buffer.
 * Reading UDR also clears the RXC flag. If the buffer is full the byte is
 * dropped (preferable to blocking inside an ISR). */
ISR(USART_RXC_vect)
{
    uint8_t c = UDR;
    uint8_t next = (uint8_t)((rx_ring_head + 1) % RX_RING_SIZE);
    if (next != rx_ring_tail)
    {
        rx_ring[rx_ring_head] = c;
        rx_ring_head = next;
    }
    /* else: ring full, drop byte */
}

uint8_t uart_rx_ready(void)
{
    return (rx_ring_head != rx_ring_tail) ? 1 : 0;
}

uint8_t uart_rx(void)
{
    uint8_t c;
    while (rx_ring_head == rx_ring_tail); /* wait for a byte (callers check ready first) */
    c = rx_ring[rx_ring_tail];
    rx_ring_tail = (uint8_t)((rx_ring_tail + 1) % RX_RING_SIZE);
    return c;
}

void adc_init(void)
{
    ADMUX = (1 << REFS0);
    ADCSRA = (1 << ADEN) | (1 << ADPS2) | (1 << ADPS1);
}

uint16_t adc_read(uint8_t ch)
{
    ch &= 0x07;
    ADMUX = (ADMUX & 0xF8) | ch;
    ADCSRA |= (1 << ADSC);
    while (ADCSRA & (1 << ADSC));
    return ADC;
}

/* ==================== CRC-8 (poly 0x07) ==================== */

uint8_t crc8(const uint8_t *data, uint8_t len)
{
    uint8_t crc = 0x00;
    for (uint8_t i = 0; i < len; i++)
    {
        crc ^= data[i];
        for (uint8_t bit = 0; bit < 8; bit++)
        {
            if (crc & 0x80)
                crc = (crc << 1) ^ 0x07;
            else
                crc = crc << 1;
        }
    }
    return crc;
}

/* ==================== COBS ==================== */

uint8_t cobs_encode(const uint8_t *input, uint8_t in_len, uint8_t *output, uint8_t out_max)
{
    uint8_t code_idx = 0;
    uint8_t code = 1;
    uint8_t out_idx = 1;

    if (out_max < in_len + 2) return 0;

    for (uint8_t i = 0; i < in_len; i++)
    {
        if (input[i] == 0x00)
        {
            output[code_idx] = code;
            code = 1;
            code_idx = out_idx++;
        }
        else
        {
            output[out_idx++] = input[i];
            code++;
            if (code == 0xFF)
            {
                output[code_idx] = code;
                code = 1;
                code_idx = out_idx++;
            }
        }
    }
    output[code_idx] = code;
    return out_idx;
}

uint8_t cobs_decode(const uint8_t *input, uint8_t in_len, uint8_t *output, uint8_t out_max)
{
    uint8_t out_idx = 0;
    uint8_t i = 0;

    while (i < in_len)
    {
        uint8_t code = input[i++];
        if (code == 0) return 0;

        for (uint8_t j = 1; j < code; j++)
        {
            if (i >= in_len || out_idx >= out_max) return 0;
            output[out_idx++] = input[i++];
        }

        if (code < 0xFF && i < in_len)
        {
            if (out_idx >= out_max) return 0;
            output[out_idx++] = 0x00;
        }
    }

    if (out_idx > 0 && output[out_idx - 1] == 0x00)
        out_idx--;

    return out_idx;
}

/* ==================== Packet TX/RX ==================== */

void send_packet(uint8_t type, const uint8_t *payload, uint8_t pay_len)
{
    uint8_t raw[MAX_RAW];
    uint8_t cobs_out[MAX_COBS];

    raw[0] = type;
    raw[1] = tx_seq++;
    raw[2] = pay_len;
    for (uint8_t i = 0; i < pay_len; i++)
        raw[3 + i] = payload[i];

    uint8_t raw_len = 3 + pay_len;
    raw[raw_len] = crc8(raw, raw_len);
    raw_len++;

    uint8_t cobs_len = cobs_encode(raw, raw_len, cobs_out, MAX_COBS);

    for (uint8_t i = 0; i < cobs_len; i++)
        uart_tx(cobs_out[i]);
    uart_tx(COBS_DELIM);
}

void send_simple(uint8_t type)
{
    send_packet(type, (const uint8_t*)0, 0);
}

/* Returns 1 if a complete frame was received, decoded packet in *out_type, *out_payload, *out_pay_len */
uint8_t try_receive(uint8_t *out_type, uint8_t *out_payload, uint8_t *out_pay_len)
{
    while (uart_rx_ready())
    {
        uint8_t b = uart_rx();
        if (b == COBS_DELIM)
        {
            if (rx_len > 0)
            {
                uint8_t raw[MAX_RAW];
                uint8_t raw_len = cobs_decode(rx_buf, rx_len, raw, MAX_RAW);
                rx_len = 0;

                if (raw_len >= 4)
                {
                    uint8_t expected_crc = crc8(raw, raw_len - 1);
                    if (raw[raw_len - 1] == expected_crc)
                    {
                        *out_type = raw[0];
                        *out_pay_len = raw[2];
                        for (uint8_t i = 0; i < raw[2]; i++)
                            out_payload[i] = raw[3 + i];
                        return 1;
                    }
                }
            }
        }
        else
        {
            if (rx_len < MAX_COBS)
                rx_buf[rx_len++] = b;
            else
                rx_len = 0;
        }
    }
    return 0;
}

/* ==================== Main ==================== */

int main(void)
{
    uint8_t pkt_type, pkt_payload[8], pkt_pay_len;
    uint16_t heartbeat_counter = 0;

    DDRA = 0x00;
    DDRD |= (1 << PD1);

    adc_init();
    uart_init();
    sei(); /* enable global interrupts so the UART RX ISR can run */
    _delay_ms(500);

    state = STATE_DISCONNECTED;

    while (1)
    {
        /* Check for incoming packets */
        if (try_receive(&pkt_type, pkt_payload, &pkt_pay_len))
        {
            switch (pkt_type)
            {
                case PKT_HELLO:
                    /* Server initiated handshake */
                    state = STATE_HANDSHAKING;
                    send_simple(PKT_READY);
                    state = STATE_CONNECTED;
                    break;

                case PKT_WELCOME:
                    /* Server acknowledged our HELLO */
                    if (state == STATE_HANDSHAKING)
                    {
                        send_simple(PKT_READY);
                        state = STATE_CONNECTED;
                    }
                    break;

                case PKT_START_STREAM:
                    if (state == STATE_CONNECTED)
                        state = STATE_STREAMING;
                    break;

                case PKT_STOP_STREAM:
                    if (state == STATE_STREAMING)
                        state = STATE_CONNECTED;
                    break;

                case PKT_PING:
                    send_simple(PKT_PONG);
                    break;

                default:
                    break;
            }
        }

        /* State-based behavior */
        switch (state)
        {
            case STATE_DISCONNECTED:
                send_simple(PKT_HELLO);
                state = STATE_HANDSHAKING;
                _delay_ms(1000);
                break;

            case STATE_HANDSHAKING:
                /* Waiting for WELCOME/HELLO response */
                _delay_ms(100);
                heartbeat_counter++;
                if (heartbeat_counter > 50)
                {
                    state = STATE_DISCONNECTED;
                    heartbeat_counter = 0;
                }
                break;

            case STATE_CONNECTED:
                /* Idle, waiting for START_STREAM */
                _delay_ms(100);
                break;

            case STATE_STREAMING:
            {
                uint16_t sensor_val = adc_read(0);
                uint8_t percent = (uint8_t)((sensor_val * 100UL) / 1023);
                if (percent > 100) percent = 100;

                uint8_t payload[1];
                payload[0] = percent;
                send_packet(PKT_DATA, payload, 1);

                _delay_ms(100);
                break;
            }
        }
    }

    return 0;
}
