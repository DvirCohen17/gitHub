#include "Communicator.h"
#pragma comment (lib, "ws2_32.lib")
#pragma comment(lib, "winhttp.lib")

#include "WSAInitializer.h"
#include <iostream>
#include <exception>
#include "SqliteDataBase.h"

int main() {
    WSAInitializer wsaInit;
    Communicator com;

    IDatabase* database = new SqliteDataBase();
    database->open();
    com.setDB(database);

    std::thread listner(&Communicator::startHandleRequests, &com);
    listner.detach();
    std::string inp;

    std::thread cloudThread(&Communicator::saveFiles, &com);
    cloudThread.detach();

    std::thread connectionThread(&Communicator::checkClientsConnection, &com);
    connectionThread.detach();

    while (inp != "EXIT")
    {
        std::cout << "Enter EXIT to quit." << std::endl;
        std::cin >> inp;
    }
}
