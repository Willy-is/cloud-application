# Cloud Application

This project is one of the assessments resolved at university. </br>
It is an application that helps compute a set of allocations that consumes a minimum amount of energy where RAM constraints are satisfied within a program duration (5 minutes). Greedy algorithm is used to find a valid set of allocation among the given

## Application

In client side, there is a dropdown menu to get [Configuration files URL](https://sit323sa.blob.core.windows.net/at2/TestSmall.cff) stored in an Azure blob. Once "Generate Allocations" button is clicked on, several asynchronous requests (configuration data) are delivered to different `WCF Services` run on AWS virtual machines and the services will return a set of allocations. </br>
Requests are passed in parallel via _asynchronous behaviour_ in WCF Service so that time to receive responses depends on the computation time of the greedy algorithm. To ensure all the responses received, thread is used to freeze the application until all the responses received or the program duration (5 minutes).

<img width="600" alt="Client GUI" src="https://user-images.githubusercontent.com/57608628/148644082-400547d0-7090-4463-879f-75938a73250c.png">

## AWS Architecture

In general, client requests are directly sent to the application load balancer. Each request is sent to the Microsoft IIS web server on a VM, then to the WCF Service. </br>
According to the architecture below, it has 3 different types of VM; t2.nano, t2.micro and t2.small. To be efficient, AWS autoscaling group:

- commences with one VM
- scales VM when CPU utilisation exceeds 70%
- terminates one VM when CPU utilisation is less than 30%
- retains one VM after response

<img width="600" alt="AWS architecture" src="https://user-images.githubusercontent.com/57608628/148643941-ec1c328f-058c-48fa-adaf-cd43af082f30.png">
