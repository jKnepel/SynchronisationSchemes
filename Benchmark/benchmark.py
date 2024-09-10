import subprocess
import socket
import struct
import time
import sys
import os

LoadScene = b'\x00'
UnloadScene = b'\x01'
StartHost = b'\x02'
StartClient = b'\x03'
StopHost = b'\x04'
StopClient = b'\x05'
GetBenchmark = b'\x06'
DirectionalInput = b'\x07'
SetObjectNumber = b'\x08'

exe_path = r"../Builds/SynchronisationSchemes.exe"
warmup_runs = 10
runs = 10

def main():
    script_directory = os.path.dirname(os.path.abspath(__file__)) 
    os.chdir(script_directory) # change working directory to file

    port1 = 9989
    port2 = 9990
    process1 = subprocess.Popen([exe_path, str(port1)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    process2 = subprocess.Popen([exe_path, str(port2)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    time.sleep(2) # load processes

    process1_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process1_socket.connect(('localhost', port1))
    process2_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    process2_socket.connect(('localhost', port2))
    time.sleep(2) # load sockets

    benchmark(process1_socket, process2_socket, 'ClientServerSimulation', 10)

    process1.terminate()
    process2.terminate()

def benchmark(process1: socket, process2: socket, scene: str, number_objects: int):
    try:
        scene_bytes = scene.encode('utf-8')
        process1.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
        process2.sendall(LoadScene + struct.pack('B', len(scene_bytes)) + scene_bytes)
        time.sleep(1) # wait for scene to be loaded

        process1.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
        process2.sendall(SetObjectNumber + struct.pack('B', 4) + struct.pack('I', number_objects))
        process1.sendall(StartHost + struct.pack('B', 0))
        process2.sendall(StartClient + struct.pack('B', 0))

        directional_input(process2, 1.0, 0, 5)
        directional_input(process2, 0, 0.5, 2)

        #response = client_socket.recv(1024)
        #print(f"Received: {response.decode('utf-8')}")
        process1.sendall(UnloadScene + struct.pack('B', 0))
        process2.sendall(UnloadScene + struct.pack('B', 0))
        time.sleep(5)
    finally:
        process1.close()
        process2.close()

def directional_input(socket: socket, up: float, right: float, duration: float):
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', up) + struct.pack('f', right))
    time.sleep(duration)
    socket.sendall(DirectionalInput + struct.pack('B', 8) + struct.pack('f', 0) + struct.pack('f', 0))

if __name__ == "__main__":
    sys.exit(main())